# ────────────────────────────────────────────────────────────────
#  Sumo2UnityTool_combined.py
#  GUI  +  SUMO ⇆ Unity simulation  (one file)
#  Version : Sumo2Unity v2.0.0
#  Author  : Ahmad Mohammadi, PhD – York University
#  License : MIT
# ────────────────────────────────────────────────────────────────
import json
import logging
import os
import queue
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

import zmq  # pip install pyzmq

# ════════════════════════════════════════════════════════════════
#  DEFAULTS (shared by GUI & simulation)
# ════════════════════════════════════════════════════════════════
DEFAULTS = {
    "IntegrationStartTime": 540,
    "ExperimentStartTime": 600,
    "ExperimentEndTime": 0,  # 0 = no time limit; set to a positive value (seconds) to stop after that sim time
    "steplength": 0.1,
    "lateral_resolution": 0.3,
    "zoom": 150.0,  # (bigger value → closer)
    "subscribe_radius": 250.0,  # ★ NEW (TraCI context radius)
}
VERSION = "Sumo2Unity v2.0.0"


# ═════════════════ GUI  SET-UP ══════════════════════════════════
root = tk.Tk()
root.title("Sumo2Unity Tool")
root.resizable(True, True)

ttk.Label(root, text=VERSION, font=("TkDefaultFont", 12, "bold")).grid(
    row=0, column=0, columnspan=4, pady=(6, 12)
)

root.columnconfigure(1, weight=1)

# ── Scenario folder picker ─────────────────────────────────────
_SCENARIOS_ROOT = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "Scenarios")
)


def _discover_scenarios():
    """Return {display_name: full_path} for subfolders containing Sumo2Unity.sumocfg."""
    found = {}
    if os.path.isdir(_SCENARIOS_ROOT):
        for name in sorted(os.listdir(_SCENARIOS_ROOT)):
            candidate = os.path.join(_SCENARIOS_ROOT, name)
            if os.path.isdir(candidate) and os.path.isfile(
                os.path.join(candidate, "Sumo2Unity.sumocfg")
            ):
                found[name] = candidate
    return found


_scenario_map = _discover_scenarios()  # {name: path}
_scenario_names = list(_scenario_map.keys())

scenario_display_var = tk.StringVar(value=_scenario_names[0] if _scenario_names else "")

ttk.Label(root, text="Scenario").grid(row=1, column=0, sticky="e", padx=6, pady=3)
scenario_combo = ttk.Combobox(
    root, textvariable=scenario_display_var, values=_scenario_names, state="readonly"
)
scenario_combo.grid(row=1, column=1, sticky="we", padx=6, pady=3)


def _get_scenario_dir() -> str:
    """Resolve display name to full path, or return raw value if browsed."""
    val = scenario_display_var.get().strip()
    return _scenario_map.get(val, val)


def browse_scenario():
    d = filedialog.askdirectory(
        initialdir=_SCENARIOS_ROOT, title="Select scenario folder"
    )
    if d:
        name = os.path.basename(d)
        if name not in _scenario_map:
            _scenario_map[name] = d
            _scenario_names.append(name)
            scenario_combo["values"] = _scenario_names
        scenario_display_var.set(name)


ttk.Button(root, text="Browse…", command=browse_scenario).grid(
    row=1, column=2, padx=6, pady=3
)

entries, row = {}, 2
for k, v in DEFAULTS.items():
    label_text = (
        "zoom (bigger value → closer)"
        if k == "zoom"
        else "subscribe radius (m)"
        if k == "subscribe_radius"
        else k
    )
    ttk.Label(root, text=label_text).grid(row=row, column=0, sticky="e", padx=6, pady=3)
    e = ttk.Entry(root)
    e.insert(0, str(v))
    e.grid(row=row, column=1, sticky="we", padx=6, pady=3)
    entries[k] = e
    row += 1

# ── NEW OPTIONS ────────────────────────────────────────────────
use_gui_var = tk.BooleanVar(value=True)
rtf_var = tk.BooleanVar(value=True)
free_cam_var = tk.BooleanVar(value=False)  # ★ NEW (Free-cam)

ttk.Checkbutton(root, text="Run SUMO with GUI", variable=use_gui_var).grid(
    row=row, column=0, columnspan=2, sticky="w", padx=6, pady=3
)
row += 1
ttk.Checkbutton(root, text="Calculate RTF", variable=rtf_var).grid(
    row=row, column=0, columnspan=2, sticky="w", padx=6, pady=3
)
row += 1
ttk.Checkbutton(
    root, text="Free camera (no follow ego vehicle)", variable=free_cam_var
).grid(row=row, column=0, columnspan=2, sticky="w", padx=6, pady=3)
row += 1  # ★ NEW
# ────────────────────────────────────────────────────────────────


# ═════════════════ SIMULATION (run_sim) ═════════════════════════
# ---------- threading state ----------
_sim_thread = None
_stop_event = threading.Event()


def run_sim(cfg: dict, stop_event=None):
    import traci
    from traci.constants import VAR_ANGLE, VAR_POSITION3D, VAR_TYPE

    if stop_event is None:
        stop_event = threading.Event()

    # ---------- apply GUI parameters ----------
    IntegrationStartTime = cfg["IntegrationStartTime"]
    ExperimentStartTime = cfg["ExperimentStartTime"]
    ExperimentEndTime = cfg["ExperimentEndTime"]
    steplength = cfg["steplength"]
    lateral_resolution = cfg["lateral_resolution"]
    zoom_level = cfg["zoom"]
    subscribe_radius = cfg["subscribe_radius"]  # ★ NEW
    use_gui = cfg["use_gui"]
    calc_rtf = cfg["calc_rtf"]
    free_cam = cfg["free_cam"]  # ★ NEW

    # ---------- logging ----------
    logging.basicConfig(
        level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s"
    )
    logger = logging.getLogger(__name__)

    # ---------- SUMO paths ----------
    if "SUMO_HOME" not in os.environ:
        sys.exit("Set SUMO_HOME env variable.")
    sys.path.append(os.path.join(os.environ["SUMO_HOME"], "tools"))

    scenario_dir = cfg["scenario_dir"]
    sumo_cfg = os.path.join(scenario_dir, "Sumo2Unity.sumocfg")
    sumo_bin = "sumo-gui" if use_gui else "sumo"
    sumo_cmd = [
        sumo_bin,
        "-c",
        sumo_cfg,
        "--step-length",
        str(steplength),
        "--lateral-resolution",
        str(lateral_resolution),
    ]
    if use_gui:
        sumo_cmd += ["--delay", "0"]  # keep 0-delay only when GUI present

    # ---------- connect TraCI ----------
    try:
        traci.start(sumo_cmd)
    except traci.exceptions.TraCIException:
        # stale connection from a previous run that didn't close cleanly
        try:
            traci.close()
        except Exception:
            pass
        traci.start(sumo_cmd)
    except traci.exceptions.FatalTraCIError:
        logger.info("SUMO connection closed.")
        try:
            root.after(0, _on_sim_finished)
        except Exception:
            pass
        return

    # ---------- gui camera helper ----------
    ego = "f_0.0"

    if use_gui and not free_cam:  # ★ NEW
        view_id = "View #0"
        traci.gui.trackVehicle(view_id, ego)
        traci.gui.setSchema(view_id, "real world")

    # ★ updated helper respects free_cam flag
    def cam_follow(view_id, veh_id):
        if free_cam:
            return
        try:
            traci.gui.trackVehicle(view_id, veh_id)
            traci.gui.setZoom(view_id, zoom_level)
        except traci.TraCIException:
            pass

    # ------------------------------------------------------------

    # ---------- TraCI context subscription ----------
    traci.vehicle.subscribeContext(
        ego,
        traci.constants.CMD_GET_VEHICLE_VARIABLE,
        subscribe_radius,  # ★ NEW (was 250)
        [VAR_POSITION3D, VAR_ANGLE, VAR_TYPE],
    )
    # ---------- ZMQ sockets ----------
    ctx = zmq.Context()
    pub = ctx.socket(zmq.PUB)
    pub.bind("tcp://*:5556")
    rout = ctx.socket(zmq.ROUTER)
    rout.bind("tcp://*:5557")

    # scenario config message (sent repeatedly during warm-up)
    scenario_name = os.path.basename(scenario_dir)
    config_msg = json.dumps(
        {"type": "config", "scenario": scenario_name}, separators=(",", ":")
    )

    # ---------- background Unity RX ----------
    u_q = queue.Queue()

    def rx_unity():
        while True:
            try:
                _ident, msg = rout.recv_multipart()
                u_q.put(json.loads(msg.decode()))
            except zmq.ZMQError:
                break
            except Exception:
                logger.exception("Unity RX")

    threading.Thread(target=rx_unity, daemon=True).start()

    # ---------- helpers ----------
    last_pos_z = {}
    prof = {k: [] for k in ("Unity", "Step", "Collect", "Send", "DataProc", "Total")}

    def sleep_precise(d):
        t0 = time.perf_counter()
        while (rem := d - (time.perf_counter() - t0)) > 0:
            if rem > 0.002:
                time.sleep(0.001)

    # ---------- results dir / RTF file ----------
    if calc_rtf:
        res_dir = os.path.join(
            os.path.abspath(os.path.join(scenario_dir, os.pardir)), "Results"
        )
        os.makedirs(res_dir, exist_ok=True)
        rtf_f = open(os.path.join(res_dir, "rtf_report.txt"), "w", encoding="utf-8")
        rtf_f.write("Time(s);RTF\n")
    else:
        rtf_f = None

    # ---------- containers ----------
    start_rec_sent = False
    start_sim_t = start_wall_t = None
    rtf_started = False
    last_sim, last_wall = 0, 0

    # ---------- main loop ----------
    STEP = steplength
    next_step = time.perf_counter() + STEP
    TL_INT = 1.0
    last_tl_t = 0.0

    try:
        # ---------- warm-up ----------
        while (
            traci.simulation.getTime() < IntegrationStartTime
            and not stop_event.is_set()
        ):
            traci.simulationStep()
            cam_follow("View #0", ego) if use_gui else None
            # keep sending config so Unity receives it despite slow-joiner
            pub.send_string(config_msg)

        while (
            traci.simulation.getMinExpectedNumber() > 0
            and (
                ExperimentEndTime <= 0 or traci.simulation.getTime() < ExperimentEndTime
            )
            and not stop_event.is_set()
        ):
            loop_t0 = time.perf_counter()
            sim_t = traci.simulation.getTime()

            # ❶ Unity → SUMO positions
            t0 = time.perf_counter()
            while not u_q.empty():
                for v in u_q.get().get("vehicles", []):
                    if v["vehicle_id"] == ego:
                        traci.vehicle.moveToXY(
                            ego,
                            "",
                            0,
                            float(v["position"][0]),
                            float(v["position"][1]),
                            float(v["angle"]),
                            keepRoute=2,
                        )
            prof["Unity"].append(time.perf_counter() - t0)

            # ❷ SUMO step
            t0 = time.perf_counter()
            traci.simulationStep()
            prof["Step"].append(time.perf_counter() - t0)
            if use_gui:
                cam_follow("View #0", ego)

            # ❸ send START_RECORDING after warm-up (independent of RTF)
            if sim_t >= ExperimentStartTime and not start_rec_sent:
                pub.send_string(
                    json.dumps({"type": "command", "command": "START_RECORDING"})
                )
                start_rec_sent = True

            # ❹ initialise RTF after warm-up (only if enabled)
            if calc_rtf and (not rtf_started) and sim_t >= ExperimentStartTime:
                rtf_started = True
                start_sim_t = sim_t
                start_wall_t = time.perf_counter()
                last_sim, last_wall = sim_t, start_wall_t

            # ❺ collect ego + context vehicles
            t0 = time.perf_counter()
            vlist = traci.vehicle.getIDList()
            vdata = []
            if ego in vlist:
                x, y, z = traci.vehicle.getPosition3D(ego)
                ang = traci.vehicle.getAngle(ego)
                vtype = traci.vehicle.getTypeID(ego)
                vdata.append(
                    {
                        "vehicle_id": ego,
                        "position": (round(x, 2), round(y, 2), round(z, 2)),
                        "angle": round(ang, 2),
                        "type": vtype,
                        "timestamp": round(time.time(), 2),
                    }
                )
                ctx_res = traci.vehicle.getContextSubscriptionResults(ego)
                if ctx_res:
                    for vid in ctx_res.keys():
                        if vid == ego:
                            continue
                        x, y, z = traci.vehicle.getPosition3D(vid)
                        ang = traci.vehicle.getAngle(vid)
                        vtype = traci.vehicle.getTypeID(vid)
                        vlong = traci.vehicle.getSpeed(vid)
                        vlat = traci.vehicle.getLateralSpeed(vid)
                        if vid in last_pos_z:
                            pz, pt = last_pos_z[vid]
                            dt = sim_t - pt
                            vvert = (z - pz) / dt if dt > 0 else 0.0
                        else:
                            vvert = 0.0
                        last_pos_z[vid] = (z, sim_t)
                        vdata.append(
                            {
                                "vehicle_id": vid,
                                "position": (round(x, 3), round(y, 3), round(z, 3)),
                                "angle": round(ang, 3),
                                "type": vtype,
                                "long_speed": round(vlong, 2),
                                "vert_speed": round(vvert, 3),
                                "lat_speed": round(vlat, 2),
                            }
                        )
            vjson = json.dumps(
                {"type": "vehicles", "vehicles": vdata}, separators=(",", ":")
            )
            prof["Collect"].append(time.perf_counter() - t0)

            # ❻ traffic lights once per second
            if sim_t - last_tl_t >= TL_INT:
                tls = [
                    {
                        "junction_id": tl,
                        "state": traci.trafficlight.getRedYellowGreenState(tl),
                    }
                    for tl in traci.trafficlight.getIDList()
                ]
                pub.send_string(
                    json.dumps(
                        {"type": "trafficlights", "lights": tls}, separators=(",", ":")
                    )
                )
                last_tl_t = sim_t

            # ❼ publish vehicles
            t0 = time.perf_counter()
            pub.send_string(vjson)
            prof["Send"].append(time.perf_counter() - t0)

            # ❽ incremental RTF (if enabled)
            if calc_rtf and rtf_started and sim_t >= ExperimentStartTime:
                now = time.perf_counter()
                if sim_t == ExperimentStartTime:
                    rtf_f.write("0.00;0.00\n")
                else:
                    sim_d = sim_t - last_sim
                    real_d = now - last_wall
                    rtf_f.write(
                        f"{sim_t - ExperimentStartTime:.2f};{sim_d / real_d:.2f}\n"
                    )
                last_sim, last_wall = sim_t, now

            # ❾ step pacing
            sleep_precise(max(0.0, next_step - time.perf_counter()))
            next_step += STEP
            prof["Total"].append(time.perf_counter() - loop_t0)

    except KeyboardInterrupt:
        logger.info("Interrupted by user.")
    except traci.exceptions.FatalTraCIError:
        logger.info("SUMO connection closed.")
    finally:
        # overall RTF
        if calc_rtf and rtf_started:
            try:
                total_w = time.perf_counter() - start_wall_t
                total_sim = traci.simulation.getTime() - start_sim_t
                if total_w > 0:
                    logger.info("RTF overall %.2f", total_sim / total_w)
            except Exception:
                pass
        if start_rec_sent:
            try:
                pub.send_string(
                    json.dumps({"type": "command", "command": "STOP_RECORDING"})
                )
            except Exception:
                pass
        if rtf_f:
            rtf_f.close()
        try:
            traci.close()
        except Exception:
            pass
        try:
            pub.close()
            rout.close()
            ctx.term()
        except Exception:
            pass
        logger.info("Finished, connections closed.")
        # notify GUI we're done (thread-safe)
        try:
            root.after(0, _on_sim_finished)
        except Exception:
            pass


# ═════════════════════ GUI → START BTN ═════════════════════════

# --- status label ---
status_var = tk.StringVar(value="Ready")
status_lbl = ttk.Label(root, textvariable=status_var, foreground="#333")
status_lbl.grid(row=row, column=0, columnspan=3, sticky="w", padx=6)
row += 1


def _on_sim_finished():
    """Called on the main thread when run_sim exits."""
    status_var.set("Simulation finished \u2014 click Start to run again")
    start_btn.config(state="normal", text="Start simulation")


def start_clicked():
    global _sim_thread, _stop_event
    try:
        cfg = {
            k: (int(v.get()) if "Time" in k else float(v.get()))
            for k, v in entries.items()
        }
        cfg["use_gui"] = bool(use_gui_var.get())
        cfg["calc_rtf"] = bool(rtf_var.get())
        cfg["free_cam"] = bool(free_cam_var.get())
        cfg["scenario_dir"] = _get_scenario_dir()
    except ValueError:
        messagebox.showerror("Invalid input", "Please enter numeric values.")
        return
    if not cfg["scenario_dir"] or not os.path.isdir(cfg["scenario_dir"]):
        messagebox.showerror(
            "Missing scenario", "Please select a valid scenario folder."
        )
        return
    _stop_event = threading.Event()
    status_var.set("Running...")
    start_btn.config(state="disabled")
    _sim_thread = threading.Thread(target=run_sim, args=(cfg, _stop_event), daemon=True)
    _sim_thread.start()


# buttons
start_btn = ttk.Button(root, text="Start simulation", command=start_clicked)
start_btn.grid(row=row, column=0, columnspan=4, pady=12, padx=6, sticky="ew", ipady=12)

root.update_idletasks()
root.geometry(
    "+{}+{}".format(
        (root.winfo_screenwidth() - root.winfo_width()) // 2,
        (root.winfo_screenheight() - root.winfo_height()) // 2,
    )
)
root.mainloop()

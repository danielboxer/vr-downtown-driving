# ────────────────────────────────────────────────────────────────
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
    "ExperimentEndTime": 0,  # 0 = no time limit; set to a positive value (seconds) to stop after that sim time
    "subscribe_radius": 120.0,
}
# Fields below are hidden from the GUI but still passed to run_sim unchanged.
_HIDDEN_DEFAULTS = {
    "ExperimentStartTime": 600,
    "steplength": 0.1,
    "lateral_resolution": 0.3,
    "zoom": 150.0,  # SUMO GUI camera zoom (bigger = closer)
}


# ═════════════════ GUI  SET-UP ══════════════════════════════════
root = tk.Tk()
root.title("Scenario Manager")
root.resizable(True, True)
root.minsize(420, 0)

# Set window icon. When frozen by PyInstaller (--onefile), bundled data is
# extracted to sys._MEIPASS; otherwise look next to the source file.
if getattr(sys, "frozen", False):
    _icon_path = os.path.join(sys._MEIPASS, "icon.png")
else:
    _icon_path = os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "icon.png"
    )
if os.path.isfile(_icon_path):
    root.iconphoto(True, tk.PhotoImage(file=_icon_path))

_style = ttk.Style()
root.option_add("*Font", ("Segoe UI", 10))
_style.configure("TEntry", padding=4)
_style.configure("TButton", padding=6)

ttk.Label(root, text="Scenario Manager", font=("Segoe UI", 12, "bold")).grid(
    row=0, column=0, columnspan=4, pady=(12, 8)
)

root.columnconfigure(1, weight=1)

# ── Scenario folder picker ─────────────────────────────────────
# When running as a PyInstaller bundle, __file__ points inside a temp extraction
# dir, so we derive the path from sys.executable instead. In dev mode (plain
# script), __file__ is simulation/python/main.py so ../Scenarios is correct.
if getattr(sys, "frozen", False):
    _SCENARIOS_ROOT = os.path.join(os.path.dirname(sys.executable), "Scenarios")
else:
    _SCENARIOS_ROOT = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "Scenarios")
    )

# Collect all scenario subfolders that contain a .rou.xml file
_scenario_names = sorted(
    (
        name
        for name in (
            os.listdir(_SCENARIOS_ROOT) if os.path.isdir(_SCENARIOS_ROOT) else []
        )
        if os.path.isdir(os.path.join(_SCENARIOS_ROOT, name))
        and any(
            f.endswith(".rou.xml")
            for f in os.listdir(os.path.join(_SCENARIOS_ROOT, name))
        )
    ),
    # Sort alphabetically by scenario base name, but _car before _bike within the same base.
    key=lambda n: (
        (n[:-4], 0)
        if n.endswith("_car")
        else (n[:-5], 1)
        if n.endswith("_bike")
        else (n, 2)
    ),
)

_default_scenario = (
    os.path.join(_SCENARIOS_ROOT, _scenario_names[0])
    if _scenario_names
    else _SCENARIOS_ROOT
)
scenario_dir_var = tk.StringVar(value=_default_scenario)

ttk.Label(root, text="Scenario").grid(row=1, column=0, sticky="e", padx=6, pady=6)
_combo = ttk.Combobox(root, values=_scenario_names, state="readonly")
_combo.grid(row=1, column=1, sticky="we", padx=6, pady=6)
if _scenario_names:
    _combo.set(_scenario_names[0])


def _on_combo_select(event=None):
    scenario_dir_var.set(os.path.join(_SCENARIOS_ROOT, _combo.get()))


_combo.bind("<<ComboboxSelected>>", _on_combo_select)


def browse_scenario():
    d = filedialog.askdirectory(
        initialdir=_SCENARIOS_ROOT, title="Select scenario folder"
    )
    if d:
        scenario_dir_var.set(d)
        # update combobox display if it matches a known scenario
        name = os.path.basename(d)
        if name in _scenario_names:
            _combo.set(name)
        else:
            _combo.set("")


ttk.Button(root, text="Browse…", command=browse_scenario).grid(
    row=1, column=2, padx=6, pady=6
)


def _get_scenario_dir() -> str:
    return scenario_dir_var.get().strip()


_sumo_installed = "SUMO_HOME" in os.environ


_FIELD_LABELS = {
    "IntegrationStartTime": "Unity Start Time",
    "ExperimentEndTime": "Scenario End Time",
    "subscribe_radius": "Subscribe Radius (m)",
}

entries, row = {}, 2
for k, v in DEFAULTS.items():
    label_text = _FIELD_LABELS.get(k, k)
    ttk.Label(root, text=label_text).grid(row=row, column=0, sticky="e", padx=6, pady=6)
    e = ttk.Entry(root)
    e.insert(0, str(v))
    e.grid(row=row, column=1, sticky="we", padx=6, pady=6)
    entries[k] = e
    row += 1

ttk.Separator(root, orient="horizontal").grid(
    row=row, column=0, columnspan=4, sticky="ew", pady=(6, 2), padx=6
)
row += 1

use_gui_var = tk.BooleanVar(value=True)
free_cam_var = tk.BooleanVar(value=True)

ttk.Checkbutton(root, text="Run SUMO with GUI", variable=use_gui_var).grid(
    row=row, column=0, columnspan=2, sticky="w", padx=6, pady=2
)
_sumo_label_text = "SUMO: installed" if _sumo_installed else "SUMO: not found"
_sumo_label_color = "#006600" if _sumo_installed else "#cc6600"
ttk.Label(root, text=_sumo_label_text, foreground=_sumo_label_color).grid(
    row=row, column=2, padx=6, pady=2, sticky="w"
)
row += 1
ttk.Checkbutton(root, text="Free camera", variable=free_cam_var).grid(
    row=row, column=0, columnspan=2, sticky="w", padx=6, pady=2
)
row += 1


# ═════════════════ SIMULATION (run_sim) ═════════════════════════
# ---------- threading state ----------
_sim_thread = None
# set by restart_clicked() or a RESTART_SIMULATION ZMQ command
_restart_event = threading.Event()


def run_sim(cfg: dict):
    import traci
    from traci.constants import VAR_ANGLE, VAR_POSITION3D, VAR_TYPE

    # ---------- apply GUI parameters ----------
    IntegrationStartTime = cfg["IntegrationStartTime"]
    ExperimentStartTime = cfg["ExperimentStartTime"]
    ExperimentEndTime = cfg["ExperimentEndTime"]
    steplength = cfg["steplength"]
    lateral_resolution = cfg["lateral_resolution"]
    zoom_level = cfg["zoom"]
    subscribe_radius = cfg["subscribe_radius"]
    use_gui = cfg["use_gui"]
    calc_rtf = cfg["calc_rtf"]
    free_cam = cfg["free_cam"]

    # ---------- logging ----------
    logging.basicConfig(
        level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s"
    )
    logger = logging.getLogger(__name__)

    # ---------- SUMO paths ----------
    if "SUMO_HOME" not in os.environ:
        logger.error("SUMO_HOME is not set; cannot start simulation.")
        root.after(0, _on_sim_finished)
        return
    sys.path.append(os.path.join(os.environ["SUMO_HOME"], "tools"))

    scenario_dir = cfg["scenario_dir"]
    parent_dir = os.path.abspath(os.path.join(scenario_dir, os.pardir))

    # Discover shared files in the Scenarios root (any matching name)
    def _glob_one(directory, pattern):
        import glob

        matches = glob.glob(os.path.join(directory, pattern))
        return matches[0] if matches else None

    sumocfg_file = _glob_one(scenario_dir, "*.sumocfg")

    sumo_bin = "sumo-gui" if use_gui else "sumo"

    if sumocfg_file:
        # Use the scenario sumocfg so net/route/additional files are resolved
        # from the config rather than a fragile glob on the parent directory.
        sumo_cmd = [
            sumo_bin,
            "-c",
            sumocfg_file,
            "--step-length",
            str(steplength),
            "--lateral-resolution",
            str(lateral_resolution),
        ]
    else:
        # Fallback: build command from discovered files (legacy behaviour)
        poly_file = _glob_one(parent_dir, "*.poly.xml")
        net_file = _glob_one(parent_dir, "*.net.xml")
        route_file = _glob_one(scenario_dir, "*.rou.xml")

        if not net_file:
            logger.error("No *.net.xml found in %s", parent_dir)
            root.after(0, _on_sim_finished)
            return
        if not route_file:
            logger.error("No *.rou.xml found in %s", scenario_dir)
            root.after(0, _on_sim_finished)
            return

        sumo_cmd = [
            sumo_bin,
            "-n",
            net_file,
            "-r",
            route_file,
            "--step-length",
            str(steplength),
            "--lateral-resolution",
            str(lateral_resolution),
        ]
        if poly_file:
            sumo_cmd += ["-a", poly_file]

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

    if use_gui and not free_cam:
        view_id = "View #0"
        traci.gui.trackVehicle(view_id, ego)
        traci.gui.setSchema(view_id, "real world")

    # helper respects free_cam flag
    def cam_follow(view_id, veh_id):
        if free_cam:
            return
        try:
            traci.gui.trackVehicle(view_id, veh_id)
            traci.gui.setZoom(view_id, zoom_level)
        except traci.TraCIException:
            pass

    # ------------------------------------------------------------

    # ---------- ZMQ sockets ----------
    ctx = zmq.Context()
    pub = ctx.socket(zmq.PUB)
    rout = ctx.socket(zmq.ROUTER)
    try:
        pub.bind("tcp://*:5556")
        rout.bind("tcp://*:5557")
    except zmq.ZMQError as e:
        logger.error(
            "Failed to bind ZMQ sockets (ports 5556/5557 may already be in use): %s", e
        )
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
        root.after(0, _on_sim_finished)
        return

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

    def sleep_precise(d):
        t0 = time.perf_counter()
        while (rem := d - (time.perf_counter() - t0)) > 0:
            if rem > 0.002:
                time.sleep(0.001)

    # ---------- constants / RTF path ----------
    STEP = steplength
    TL_INT = 1.0
    res_dir = os.path.join(parent_dir, "Results")
    rtf_f = None

    try:
        # outer restart loop: traci.load() reloads SUMO in-place without closing
        # the process or tearing down ZMQ, so Unity stays connected across restarts
        while True:
            _restart_event.clear()

            # drain stale messages that arrived during the previous run or the
            # restart window so they don't affect the new run
            while not u_q.empty():
                u_q.get()

            # open a fresh RTF file for each run
            if rtf_f:
                rtf_f.close()
                rtf_f = None
            if calc_rtf:
                os.makedirs(res_dir, exist_ok=True)
                rtf_f = open(
                    os.path.join(res_dir, "rtf_report.txt"), "w", encoding="utf-8"
                )
                rtf_f.write("Time(s);RTF\n")

            # ---------- per-run containers ----------
            start_rec_sent = False
            start_sim_t = start_wall_t = None
            rtf_started = False
            last_sim, last_wall = 0, 0
            last_pos_z.clear()
            next_step = time.perf_counter() + STEP
            last_tl_t = 0.0

            # ---------- warm-up ----------
            while (
                traci.simulation.getTime() < IntegrationStartTime
                and not _restart_event.is_set()
            ):
                traci.simulationStep()
                cam_follow("View #0", ego) if use_gui else None
                # keep sending config so Unity receives it despite slow-joiner
                pub.send_string(config_msg)

            if _restart_event.is_set():
                # restart requested during warm-up: reload and loop back
                traci.load(sumo_cmd[1:])
                continue

            # Subscribe to ego context now that f_0.0 has been inserted (depart=IntegrationStartTime).
            # Calling subscribeContext before any simulation steps fails when SUMO uses incremental
            # route loading (large route files), because the vehicle isn't known yet at t=0.
            traci.vehicle.subscribeContext(
                ego,
                traci.constants.CMD_GET_VEHICLE_VARIABLE,
                subscribe_radius,
                [VAR_POSITION3D, VAR_ANGLE, VAR_TYPE],
            )

            # warm-up done: update status and re-enable restart button on main thread
            try:
                root.after(
                    0,
                    lambda: [
                        status_var.set("Running..."),
                        restart_btn.config(state="normal"),
                    ],
                )
            except Exception:
                pass

            while (
                traci.simulation.getMinExpectedNumber() > 0
                and (
                    ExperimentEndTime <= 0
                    or traci.simulation.getTime() < ExperimentEndTime
                )
                and not _restart_event.is_set()
            ):
                sim_t = traci.simulation.getTime()

                # ❶ Unity → SUMO positions
                while not u_q.empty():
                    msg = u_q.get()
                    # Handle RESTART_SIMULATION command sent from Unity
                    if (
                        msg.get("type") == "command"
                        and msg.get("command") == "RESTART_SIMULATION"
                    ):
                        _restart_event.set()
                        break
                    for v in msg.get("vehicles", []):
                        if v["vehicle_id"] == ego:
                            try:
                                traci.vehicle.moveToXY(
                                    ego,
                                    "",
                                    0,
                                    float(v["position"][0]),
                                    float(v["position"][1]),
                                    float(v["angle"]),
                                    keepRoute=2,
                                )
                            except traci.exceptions.TraCIException:
                                pass  # ego not yet inserted in SUMO; skip until it appears

                # ❷ SUMO step
                traci.simulationStep()
                if use_gui:
                    cam_follow("View #0", ego)

                # ❸ send START_RECORDING after warm-up (independent of RTF)
                if sim_t >= ExperimentStartTime and not start_rec_sent:
                    pub.send_string(
                        json.dumps(
                            {"type": "command", "command": "START_RECORDING"},
                            separators=(",", ":"),
                        )
                    )
                    start_rec_sent = True

                # ❹ initialise RTF after warm-up (only if enabled)
                if calc_rtf and (not rtf_started) and sim_t >= ExperimentStartTime:
                    rtf_started = True
                    start_sim_t = sim_t
                    start_wall_t = time.perf_counter()
                    last_sim, last_wall = sim_t, start_wall_t

                # ❺ collect ego + context vehicles

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
                            {"type": "trafficlights", "lights": tls},
                            separators=(",", ":"),
                        )
                    )
                    last_tl_t = sim_t

                # ❼ publish vehicles
                pub.send_string(vjson)

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

            # end of main loop (inner)

            if _restart_event.is_set():
                # restart requested mid-run: send STOP_RECORDING, then reload SUMO
                if start_rec_sent:
                    try:
                        pub.send_string(
                            json.dumps(
                                {"type": "command", "command": "STOP_RECORDING"},
                                separators=(",", ":"),
                            )
                        )
                    except Exception:
                        pass
                logger.info("Restarting simulation via traci.load()")
                try:
                    root.after(0, lambda: status_var.set("Restarting..."))
                except Exception:
                    pass
                traci.load(sumo_cmd[1:])
                continue
            # natural end of simulation (no more vehicles expected)
            break

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
                    json.dumps(
                        {"type": "command", "command": "STOP_RECORDING"},
                        separators=(",", ":"),
                    )
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
    restart_btn.config(state="disabled")
    status_var.set("Simulation finished - click Start to run again")
    start_btn.config(state="normal", text="Start simulation")


def _send_scenario_to_unity(scenario_name: str):
    """Send config message to Unity via ZMQ without starting SUMO.

    Runs on a background thread. Binds port 5556 briefly, sends the config
    message repeatedly for slow-joiner mitigation, then releases the socket.
    """
    config_msg = json.dumps(
        {"type": "config", "scenario": scenario_name}, separators=(",", ":")
    )
    try:
        ctx_temp = zmq.Context()
        pub_temp = ctx_temp.socket(zmq.PUB)
        pub_temp.bind("tcp://*:5556")
        # Brief pause so Unity's subscriber can connect (ZMQ slow-joiner mitigation)
        time.sleep(0.3)
        for _ in range(15):
            pub_temp.send_string(config_msg)
            time.sleep(0.05)
    except zmq.ZMQError as e:
        root.after(0, lambda msg=str(e): status_var.set(f"Switch failed: {msg}"))
        return
    finally:
        try:
            pub_temp.close()
            ctx_temp.term()
        except Exception:
            pass
        root.after(0, lambda: start_btn.config(state="normal", text="Start simulation"))

    notice = (
        f"Switched to '{scenario_name}' in Unity "
        "(SUMO not installed - no traffic simulation)."
    )
    root.after(0, lambda msg=notice: status_var.set(msg))


def start_clicked():
    global _sim_thread
    if not _sumo_installed:
        # SUMO absent: send the scenario config to Unity so it can switch
        # vehicles/splines, but warn the user there will be no NPC traffic.
        scenario_dir = _get_scenario_dir()
        if not scenario_dir or not os.path.isdir(scenario_dir):
            messagebox.showerror(
                "Missing scenario", "Please select a valid scenario folder."
            )
            return
        status_var.set("Sending scenario to Unity (SUMO not installed)...")
        start_btn.config(state="disabled", text="Switching...")
        threading.Thread(
            target=_send_scenario_to_unity,
            args=(os.path.basename(scenario_dir),),
            daemon=True,
        ).start()
        return
    try:
        cfg = {
            k: (int(v.get()) if "Time" in k else float(v.get()))
            for k, v in entries.items()
        }
        cfg.update(_HIDDEN_DEFAULTS)
        cfg["use_gui"] = bool(use_gui_var.get())
        cfg["calc_rtf"] = False
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
    status_var.set("Running...")
    start_btn.config(state="disabled")
    restart_btn.config(state="normal")
    _sim_thread = threading.Thread(target=run_sim, args=(cfg,), daemon=True)
    _sim_thread.start()


def restart_clicked():
    if _sim_thread and _sim_thread.is_alive():
        # signal the sim thread to call traci.load() and restart from t=0;
        # the thread stays alive so ZMQ and Unity stay connected
        _restart_event.set()
        status_var.set("Restarting...")
        restart_btn.config(state="disabled")
    else:
        # sim not currently running, just start fresh
        start_clicked()


# buttons: Start (left) and Restart (right) on the same row
start_btn = ttk.Button(root, text="Start simulation", command=start_clicked)
start_btn.grid(
    row=row, column=0, columnspan=2, pady=(12, 4), padx=6, sticky="ew", ipady=12
)
restart_btn = ttk.Button(
    root, text="Restart simulation", command=restart_clicked, state="disabled"
)
restart_btn.grid(
    row=row, column=2, columnspan=2, pady=(12, 4), padx=6, sticky="ew", ipady=12
)


root.update_idletasks()
root.geometry(
    "+{}+{}".format(
        (root.winfo_screenwidth() - root.winfo_width()) // 2,
        (root.winfo_screenheight() - root.winfo_height()) // 2,
    )
)
root.mainloop()

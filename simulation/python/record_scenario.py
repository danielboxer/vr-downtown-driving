"""Bake a SUMO scenario to a gzipped JSON-lines recording for offline replay.

The web (WebGL) build can't run SUMO, so traffic is pre-recorded here and
replayed in Unity by SimulationReplayer. Each line is one message in the same
shape main.py publishes over ZMQ, so Unity's SimulationController.HandleMessage
parses both, plus a "t" field giving playback time in seconds from 0. Keep the
field layout here in sync with main.py; the C# parser is the source of truth.

Run:
    uv run python record_scenario.py simulation/Scenarios/downtown_car
    uv run python record_scenario.py --all
"""

import argparse
import glob
import gzip
import json
import os
import sys

from sim_constants import EGO_ID, LATERAL_RESOLUTION, STEP_LENGTH

DEFAULT_SUMO_HOME = "C:\\Program Files (x86)\\Eclipse\\Sumo"
EGO_TYPE = "EgoCar"
TL_INTERVAL = 1.0


def _resolve_sumo() -> str:
    """Find a SUMO install and return the `sumo` binary to launch.

    Priority: the SUMO_HOME env var (desktop/local), then the pip `eclipse-sumo`
    package (CI), then the default Windows install path. Adds SUMO_HOME/tools to
    sys.path when present so a bundled traci is importable; a pip-installed traci
    needs neither SUMO_HOME nor that path.
    """
    homes = []
    env_home = os.environ.get("SUMO_HOME")
    if env_home:
        homes.append(env_home)
    try:
        import sumo  # provided by `pip install eclipse-sumo`

        homes.append(sumo.SUMO_HOME)
    except Exception:
        pass
    homes.append(DEFAULT_SUMO_HOME)

    for home in homes:
        if not home:
            continue
        tools = os.path.join(home, "tools")
        if os.path.isdir(tools) and tools not in sys.path:
            sys.path.insert(0, tools)
        for name in ("sumo.exe", "sumo"):
            binary = os.path.join(home, "bin", name)
            if os.path.isfile(binary):
                return binary
    return "sumo"  # last resort: rely on PATH


def _scenarios_root() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", "Scenarios"))


def _default_out(scenario_name: str) -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(
        os.path.join(
            here, "..", "Assets", "StreamingAssets", "Recordings",
            scenario_name + ".jsonl.gz",
        )
    )


def _dump(record: dict) -> str:
    return json.dumps(record, separators=(",", ":"))


def bake(scenario_dir: str, out_path: str, sumo_bin: str, start: float, duration: float, seed: int) -> int:
    try:
        import traci
        from traci.constants import (
            VAR_ANGLE,
            VAR_POSITION3D,
            VAR_SPEED,
            VAR_SPEED_LAT,
            VAR_TYPE,
        )
    except ImportError as e:
        sys.exit(f"Could not import traci ({e}). Install it with 'pip install traci' or set SUMO_HOME.")

    sub_vars = (VAR_POSITION3D, VAR_ANGLE, VAR_TYPE, VAR_SPEED, VAR_SPEED_LAT)
    scenario_name = os.path.basename(os.path.normpath(scenario_dir))
    sumocfg = glob.glob(os.path.join(scenario_dir, "*.sumocfg"))
    if not sumocfg:
        sys.exit(f"No .sumocfg found in {scenario_dir}")

    cmd = [
        sumo_bin,
        "-c", sumocfg[0],
        "--step-length", str(STEP_LENGTH),
        "--lateral-resolution", str(LATERAL_RESOLUTION),
        "--seed", str(seed),
        "--no-step-log", "true",
        "--no-warnings", "true",
    ]
    traci.start(cmd)

    end = start + duration
    last_z = {}  # vid -> (z, sim_t), used to derive vertical speed
    last_tl_t = -TL_INTERVAL
    record_t0 = None
    frames = 0

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    try:
        with gzip.open(out_path, "wt", encoding="utf-8", newline="\n") as f:
            f.write(_dump({"t": 0.0, "type": "config", "scenario": scenario_name}) + "\n")

            # Warm up silently to `start`, then record until `end` (or demand runs out).
            # Each vehicle is subscribed once on departure so per-step reads come back
            # in a single getAllSubscriptionResults call instead of ~5 round-trips each.
            while traci.simulation.getMinExpectedNumber() > 0:
                traci.simulationStep()
                for vid in traci.simulation.getDepartedIDList():
                    if vid != EGO_ID:
                        traci.vehicle.subscribe(vid, sub_vars)

                sim_t = traci.simulation.getTime()
                if sim_t < start:
                    continue
                if sim_t >= end:
                    break

                if record_t0 is None:
                    record_t0 = sim_t
                rel_t = round(sim_t - record_t0, 2)

                vehicles = []
                for vid, res in traci.vehicle.getAllSubscriptionResults().items():
                    if res[VAR_TYPE] == EGO_TYPE:
                        continue
                    x, y, z = res[VAR_POSITION3D]
                    if vid in last_z:
                        pz, pt = last_z[vid]
                        dt = sim_t - pt
                        vvert = (z - pz) / dt if dt > 0 else 0.0
                    else:
                        vvert = 0.0
                    last_z[vid] = (z, sim_t)
                    vehicles.append({
                        "vehicle_id": vid,
                        "position": (round(x, 3), round(y, 3), round(z, 3)),
                        "angle": round(res[VAR_ANGLE], 3),
                        "type": res[VAR_TYPE],
                        "long_speed": round(res[VAR_SPEED], 2),
                        "vert_speed": round(vvert, 3),
                        "lat_speed": round(res[VAR_SPEED_LAT], 2),
                    })
                f.write(_dump({"t": rel_t, "type": "vehicles", "vehicles": vehicles}) + "\n")
                frames += 1

                if sim_t - last_tl_t >= TL_INTERVAL:
                    lights = [
                        {"junction_id": tl, "state": traci.trafficlight.getRedYellowGreenState(tl)}
                        for tl in traci.trafficlight.getIDList()
                    ]
                    f.write(_dump({"t": rel_t, "type": "trafficlights", "lights": lights}) + "\n")
                    last_tl_t = sim_t
    finally:
        traci.close()

    if frames == 0:
        print(f"WARNING {scenario_name}: 0 frames (no traffic in window {start}-{end}s)")
    print(f"{scenario_name}: {frames} frames -> {out_path}")
    return frames


def _all_scenario_dirs(root: str) -> list:
    # Skip calibration scenarios: they carry no ambient traffic, so a bake would
    # replay an empty street.
    return sorted(
        os.path.join(root, name)
        for name in os.listdir(root)
        if os.path.isdir(os.path.join(root, name))
        and not name.startswith("calibration")
        and glob.glob(os.path.join(root, name, "*.sumocfg"))
    )


def main():
    p = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    p.add_argument("scenario_dir", nargs="?", help="Scenario folder containing a .sumocfg")
    p.add_argument("--all", action="store_true", help="Bake every scenario folder under Scenarios/")
    p.add_argument("--out", help="Output path (default: StreamingAssets/Recordings/<scenario>.jsonl.gz)")
    p.add_argument("--start", type=float, default=600.0,
                   help="Sim time (s) to begin recording; warm-up runs silently before this")
    p.add_argument("--duration", type=float, default=480.0, help="Recording length in seconds")
    p.add_argument("--seed", type=int, default=42, help="SUMO RNG seed for reproducible bakes")
    args = p.parse_args()

    sumo_bin = _resolve_sumo()
    print(f"Using SUMO binary: {sumo_bin}")

    if args.all:
        dirs = _all_scenario_dirs(_scenarios_root())
        if not dirs:
            sys.exit(f"No scenarios with a .sumocfg under {_scenarios_root()}")
        for d in dirs:
            bake(d, _default_out(os.path.basename(d)), sumo_bin, args.start, args.duration, args.seed)
        return

    if not args.scenario_dir:
        p.error("provide a scenario_dir or use --all")
    name = os.path.basename(os.path.normpath(args.scenario_dir))
    bake(args.scenario_dir, args.out or _default_out(name), sumo_bin, args.start, args.duration, args.seed)


if __name__ == "__main__":
    main()

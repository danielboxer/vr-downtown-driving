#!/usr/bin/env python3
"""Generate random NPC traffic and optionally augment an existing route file.

Usage examples:
    # Generate random trips for test_random_trips (default behaviour)
    python generate_random_trips.py test_random_trips

    # Higher density using flows instead of individual trips
    python generate_random_trips.py test_random_trips --flows 8 --period 2

    # Augment busy_downtown_car with extra random trips on top of its existing flows
    python generate_random_trips.py busy_downtown_car --base-file busy_downtown_car.rou.xml

    # No bikes, custom output file
    python generate_random_trips.py downtown_car --no-bikes --output downtown_car_random.rou.xml
"""

import argparse
import os
import random
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

SUMO_HOME = os.environ.get("SUMO_HOME", r"C:\Program Files (x86)\Eclipse\Sumo")
RANDOM_TRIPS_PY = os.path.join(SUMO_HOME, "tools", "randomTrips.py")
DUAROUTER = os.path.join(SUMO_HOME, "bin", "duarouter.exe")

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCENARIOS_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "Scenarios"))

DEFAULT_CAR_PERIOD = 3.0
DEFAULT_BIKE_PERIOD = 15.0
DEFAULT_START_TIME = 0.0
DEFAULT_END_TIME = 3600.0
DEFAULT_SEED = 42

CAR_VTYPES = ["301", "302", "303", "304", "305", "306"]
BIKE_VTYPE = "bike"

# The ego car's starting edge, kept clear of random traffic to prevent congestion
# at t=540 that would block ego vehicle insertion.
EGO_RESERVED_EDGES: set[str] = {"62"}

VTYPES = [
    {
        "id": "301",
        "length": "4.68",
        "width": "2.32",
        "height": "1.47",
        "color": "0,0,128",
    },
    {
        "id": "302",
        "length": "4.68",
        "width": "2.32",
        "height": "1.47",
        "color": "132,132,132",
    },
    {"id": "303", "length": "4.06", "width": "1.84", "color": "66,66,66"},
    {"id": "304", "length": "4.07", "width": "1.84", "color": "228,58,58"},
    {
        "id": "305",
        "length": "4.81",
        "guiShape": "passenger",
        "width": "2.16",
        "color": "245,215,50",
    },
    {"id": "306", "length": "4.81", "width": "2.16", "color": "229,229,229"},
    {"id": "EgoBike", "length": "1.80", "vClass": "bicycle", "color": "16,186,16"},
    {"id": "EgoCar", "length": "4.81", "width": "2.16", "color": "69,56,56"},
    {"id": "EgoScooter", "length": "1.09", "vClass": "scooter"},
    {
        "id": "bike",
        "length": "1.80",
        "maxSpeed": "6.00",
        "vClass": "bicycle",
        "width": "0.65",
        "height": "1.70",
        "color": "200,50,50",
    },
]


def resolve_scenario(scenario: str) -> tuple[str, str]:
    """Return (scenario_dir, net_file) for the given scenario name or path."""
    if os.path.isabs(scenario) or os.sep in scenario:
        scenario_dir = os.path.normpath(scenario)
    else:
        scenario_dir = os.path.join(SCENARIOS_DIR, scenario)
    if not os.path.isdir(scenario_dir):
        print(f"Error: scenario directory not found: {scenario_dir}", file=sys.stderr)
        sys.exit(1)
    parent = os.path.dirname(scenario_dir)
    net_files = [f for f in os.listdir(parent) if f.endswith(".net.xml")]
    if not net_files:
        print(f"Error: no *.net.xml found in {parent}", file=sys.stderr)
        sys.exit(1)
    return scenario_dir, os.path.join(parent, net_files[0])


def run_random_trips(
    net_file: str,
    output_file: str,
    period: float,
    start: float,
    end: float,
    seed: int,
    prefix: str,
    fringe_factor: float = 100.0,
    vehicle_class: str | None = None,
    num_flows: int = 0,
) -> None:
    """Call SUMO randomTrips.py and write the result to output_file."""
    cmd = [
        sys.executable,
        RANDOM_TRIPS_PY,
        "-n",
        net_file,
        "-o",
        output_file,
        "-b",
        str(start),
        "-e",
        str(end),
        "-p",
        str(period),
        "-s",
        str(seed),
        "--prefix",
        prefix,
        "--min-distance",
        "50",
        "--fringe-factor",
        str(fringe_factor),
    ]
    if vehicle_class:
        cmd += ["--vehicle-class", vehicle_class]
    if num_flows > 0:
        cmd += ["--flows", str(num_flows)]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(
            f"randomTrips.py error (prefix={prefix}):\n{result.stderr}", file=sys.stderr
        )
        sys.exit(1)
    if result.stderr:
        print(result.stderr.strip(), file=sys.stderr)


def validate_bike_trips(net_file: str, trips_file: str, routes_file: str) -> set[str]:
    """Run duarouter on bike trips and return the IDs of successfully routed vehicles.

    Only used for individual trips (not flows); flows don't need pre-validation because
    SUMO's online router handles unroutable departures at runtime without aborting.
    """
    cmd = [
        DUAROUTER,
        "-n",
        net_file,
        "-r",
        trips_file,
        "-o",
        routes_file,
        "--ignore-errors",  # skip unroutable trips instead of aborting
        "--no-step-log",
        "--no-warnings",
        "--vtype-output",
        os.devnull,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode not in (0, 1):  # 0=OK, 1=warnings only
        print(f"duarouter error:\n{result.stderr}", file=sys.stderr)
        sys.exit(1)
    if not os.path.exists(routes_file):
        return set()
    tree = ET.parse(routes_file)
    return {elem.get("id", "") for elem in tree.getroot().iter("vehicle")}


def parse_entries(file_path: str) -> list[dict]:
    """Parse <trip> and <flow> elements from a randomTrips output file."""
    tree = ET.parse(file_path)
    return [
        {"_tag": elem.tag, **dict(elem.attrib)}
        for elem in tree.getroot()
        if elem.tag in ("trip", "flow")
    ]


def read_base_file(base_path: str) -> list[dict]:
    """Read existing trips/flows from a route file (excludes vType definitions)."""
    if not os.path.exists(base_path):
        return []
    tree = ET.parse(base_path)
    return [
        {"_tag": elem.tag, **dict(elem.attrib)}
        for elem in tree.getroot()
        if elem.tag in ("trip", "flow")
    ]


def build_route_xml(
    base_entries: list[dict],
    car_entries: list[dict],
    bike_entries: list[dict],
    rng: random.Random,
) -> ET.Element:
    """Assemble the final <routes> element with vTypes, base entries, and random entries."""
    root = ET.Element("routes")
    root.set("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance")
    root.set("xsi:noNamespaceSchemaLocation", "http://sumo.dlr.de/xsd/routes_file.xsd")

    root.append(ET.Comment(" VTypes "))
    for attrs in VTYPES:
        vtype_elem = ET.SubElement(root, "vType")
        for k, v in attrs.items():
            vtype_elem.set(k, v)

    # Assign a random car vType to each car entry, bike vType to each bike entry
    for entry in car_entries:
        entry["type"] = rng.choice(CAR_VTYPES)
    for entry in bike_entries:
        entry["type"] = BIKE_VTYPE

    # Merge base and random entries into one list sorted by departure/begin time.
    # SUMO requires the whole route file sorted, so base entries can't be written
    # separately ahead of the sorted random ones.
    all_entries = base_entries + car_entries + bike_entries
    all_entries.sort(key=lambda e: float(e.get("depart") or e.get("begin") or 0))

    if all_entries:
        root.append(ET.Comment(" Trips / flows "))
        for attrs in all_entries:
            tag = attrs.pop("_tag")
            elem = ET.SubElement(root, tag)
            for k, v in attrs.items():
                elem.set(k, v)
            attrs["_tag"] = tag  # restore so the caller's list is not mutated

    return root


def main() -> None:
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "scenario",
        help="Scenario folder name (e.g. test_random_trips) or full path",
    )
    parser.add_argument(
        "--period",
        type=float,
        default=DEFAULT_CAR_PERIOD,
        help="Seconds between car departures (default %(default)s; lower period = denser traffic)",
    )
    parser.add_argument(
        "--bike-period",
        type=float,
        default=DEFAULT_BIKE_PERIOD,
        help="Bike departure period in seconds (default %(default)s)",
    )
    parser.add_argument(
        "--end",
        type=float,
        default=DEFAULT_END_TIME,
        help="Simulation end time in seconds (default %(default)s)",
    )
    parser.add_argument(
        "--start",
        type=float,
        default=DEFAULT_START_TIME,
        help="Earliest departure time for generated trips in seconds (default %(default)s)",
    )
    parser.add_argument(
        "--flows",
        type=int,
        default=0,
        help="Generate N continuous flows instead of individual trips (default 0 = trips)",
    )
    parser.add_argument(
        "--no-bikes",
        action="store_true",
        help="Skip bike trip/flow generation",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=DEFAULT_SEED,
        help="Random seed (default %(default)s)",
    )
    parser.add_argument(
        "--fringe-factor",
        type=float,
        default=100.0,
        help=(
            "Weight towards network-boundary edges for trip origins/destinations "
            "(default %(default)s; higher = more fringe; 1.0 = fully random edge selection)"
        ),
    )
    parser.add_argument(
        "--base-file",
        metavar="PATH",
        help=(
            "Existing route file to augment. "
            "Its trips/flows are merged with the generated ones and sorted by depart. "
            "Defaults to {scenario}.rou.xml in the scenario folder if it exists."
        ),
    )
    parser.add_argument(
        "--output",
        metavar="PATH",
        help="Output route file path (default: {scenario}.rou.xml in scenario folder)",
    )
    args = parser.parse_args()

    rng = random.Random(args.seed)
    scenario_dir, net_file = resolve_scenario(args.scenario)

    scenario_name = os.path.basename(scenario_dir)
    default_rou = os.path.join(scenario_dir, scenario_name + ".rou.xml")
    base_path = args.base_file or default_rou
    output_path = args.output or default_rou

    base_entries = read_base_file(base_path) if args.base_file else []
    if base_entries:
        print(f"  Read {len(base_entries)} existing entries from {base_path}")

    label = "flows" if args.flows > 0 else "trips"

    with tempfile.TemporaryDirectory() as tmpdir:
        car_file = os.path.join(tmpdir, "cars.xml")
        bike_file = os.path.join(tmpdir, "bikes.xml")
        bike_routes_file = os.path.join(tmpdir, "bike_routes.xml")

        print(f"Generating car {label}...")
        run_random_trips(
            net_file,
            car_file,
            args.period,
            args.start,
            args.end,
            args.seed,
            prefix="car_",
            fringe_factor=args.fringe_factor,
            num_flows=args.flows,
        )
        car_entries = parse_entries(car_file)
        # Drop trips that start or end on the ego's reserved edge
        car_entries = [
            e
            for e in car_entries
            if e.get("from") not in EGO_RESERVED_EDGES
            and e.get("to") not in EGO_RESERVED_EDGES
        ]

        bike_entries: list[dict] = []
        if not args.no_bikes:
            print(f"Generating bike {label}...")
            run_random_trips(
                net_file,
                bike_file,
                args.bike_period,
                args.start,
                args.end,
                args.seed + 1,
                prefix="bike_",
                fringe_factor=args.fringe_factor,
                vehicle_class="bicycle",
                num_flows=args.flows,
            )
            all_bike = parse_entries(bike_file)
            all_bike = [
                e
                for e in all_bike
                if e.get("from") not in EGO_RESERVED_EDGES
                and e.get("to") not in EGO_RESERVED_EDGES
            ]

            if args.flows == 0:
                # Validate individual bike trips with duarouter.
                # Flows are skipped: SUMO's online router handles unroutable departures
                # at runtime without aborting, so pre-validation isn't needed.
                print("Validating bike routes with duarouter...")
                valid_ids = validate_bike_trips(net_file, bike_file, bike_routes_file)
                bike_entries = [e for e in all_bike if e.get("id") in valid_ids]
                dropped = len(all_bike) - len(bike_entries)
                if dropped:
                    print(f"  Dropped {dropped} unroutable bike trip(s)")
            else:
                bike_entries = all_bike

    print(f"  {len(car_entries)} car {label}, {len(bike_entries)} bike {label}")

    root = build_route_xml(base_entries, car_entries, bike_entries, rng)
    ET.indent(root, space="    ")
    ET.ElementTree(root).write(output_path, encoding="UTF-8", xml_declaration=True)
    print(f"Written: {output_path}")


if __name__ == "__main__":
    main()

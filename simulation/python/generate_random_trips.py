#!/usr/bin/env python3
"""Generate random NPC traffic trips for the test_random_trips scenario.

Runs SUMO's randomTrips.py for cars and bikes, assigns custom vTypes,
and writes the combined Sumo2Unity.rou.xml.

Usage:
    python generate_random_trips.py
"""

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
NET_FILE = os.path.join(SCENARIOS_DIR, "downtown.net.xml")
OUTPUT_DIR = os.path.join(SCENARIOS_DIR, "test_random_trips")
OUTPUT_FILE = os.path.join(OUTPUT_DIR, "Sumo2Unity.rou.xml")

END_TIME = 3600.0
CAR_PERIOD = 3.0  # one car trip every 3s = ~1200 car trips/hour (busy downtown)
BIKE_PERIOD = 15.0  # one bike trip every 15s = ~240 bike trips/hour
RANDOM_SEED = 42

CAR_VTYPES = ["301", "302", "303", "304", "305", "306"]
BIKE_VTYPE = "bike"

# Ego car trip, always included
EGO_TRIP = {
    "id": "f_0.0",
    "type": "EgoCar",
    "depart": "540.00",
    "from": "62",
    "to": "62",
}

# Edges reserved for the ego car; random trips that start or end here are dropped
# to prevent congestion that would block ego insertion at t=540.
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


def run_random_trips(
    output_file: str,
    period: float,
    seed: int,
    prefix: str,
    vehicle_class: str | None = None,
) -> None:
    """Call SUMO randomTrips.py and write the result to output_file."""
    cmd = [
        sys.executable,
        RANDOM_TRIPS_PY,
        "-n",
        NET_FILE,
        "-o",
        output_file,
        "-e",
        str(END_TIME),
        "-p",
        str(period),
        "-s",
        str(seed),
        "--prefix",
        prefix,
        "--min-distance",
        "50",
    ]
    if vehicle_class:
        cmd += ["--vehicle-class", vehicle_class]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(
            f"randomTrips.py error (prefix={prefix}):\n{result.stderr}", file=sys.stderr
        )
        sys.exit(1)
    if result.stderr:
        # randomTrips.py prints progress to stderr
        print(result.stderr.strip(), file=sys.stderr)


def validate_bike_trips(trips_file: str, routes_file: str) -> set[str]:
    """Run duarouter on the bike trips and return the IDs of successfully routed vehicles."""
    cmd = [
        DUAROUTER,
        "-n",
        NET_FILE,
        "-r",
        trips_file,
        "-o",
        routes_file,
        "--ignore-errors",  # skip unroutable trips instead of aborting
        "--no-step-log",
        "--no-warnings",
        "--vtype-output",
        os.devnull,  # discard vType output
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode not in (0, 1):  # 1 = warnings only, still OK
        print(f"duarouter error:\n{result.stderr}", file=sys.stderr)
        sys.exit(1)

    if not os.path.exists(routes_file):
        return set()

    tree = ET.parse(routes_file)
    # duarouter output uses <vehicle id="..."> elements for successfully routed vehicles
    return {elem.get("id", "") for elem in tree.getroot().iter("vehicle")}


def parse_trips(file_path: str) -> list[dict]:
    """Parse <trip> elements from a randomTrips output XML file."""
    tree = ET.parse(file_path)
    return [dict(elem.attrib) for elem in tree.getroot().iter("trip")]


def main() -> None:
    rng = random.Random(RANDOM_SEED)

    os.makedirs(OUTPUT_DIR, exist_ok=True)

    with tempfile.TemporaryDirectory() as tmpdir:
        car_file = os.path.join(tmpdir, "cars.xml")
        bike_file = os.path.join(tmpdir, "bikes.xml")
        bike_routes_file = os.path.join(tmpdir, "bike_routes.xml")

        print("Generating car trips...")
        run_random_trips(car_file, CAR_PERIOD, RANDOM_SEED, prefix="car_")

        print("Generating bike trips...")
        run_random_trips(
            bike_file,
            BIKE_PERIOD,
            RANDOM_SEED + 1,
            prefix="bike_",
            vehicle_class="bicycle",
        )

        car_trips = parse_trips(car_file)
        all_bike_trips = parse_trips(bike_file)

        # Validate bike trips with duarouter: drop any trip that has no valid route
        # across the bicycle network (disconnected segments are common in OSM-derived nets).
        print("Validating bike routes with duarouter...")
        valid_bike_ids = validate_bike_trips(bike_file, bike_routes_file)
        bike_trips = [t for t in all_bike_trips if t.get("id") in valid_bike_ids]
        dropped = len(all_bike_trips) - len(bike_trips)
        if dropped:
            print(f"  Dropped {dropped} unroutable bike trip(s)")

    # Remove random trips that start or end on the ego's reserved edges
    car_trips = [
        t
        for t in car_trips
        if t.get("from") not in EGO_RESERVED_EDGES
        and t.get("to") not in EGO_RESERVED_EDGES
    ]
    bike_trips = [
        t
        for t in bike_trips
        if t.get("from") not in EGO_RESERVED_EDGES
        and t.get("to") not in EGO_RESERVED_EDGES
    ]

    print(
        f"  {len(car_trips)} car trips, {len(bike_trips)} bike trips (after edge filtering)"
    )

    # Build the combined routes XML
    root = ET.Element("routes")
    root.set("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance")
    root.set("xsi:noNamespaceSchemaLocation", "http://sumo.dlr.de/xsd/routes_file.xsd")

    root.append(ET.Comment(" VTypes "))
    for attrs in VTYPES:
        vtype_elem = ET.SubElement(root, "vType")
        for k, v in attrs.items():
            vtype_elem.set(k, v)

    # Assign a random car vType to each car trip, bike vType to each bike trip
    all_trips: list[dict] = []
    for trip in car_trips:
        trip["type"] = rng.choice(CAR_VTYPES)
        all_trips.append(trip)
    for trip in bike_trips:
        trip["type"] = BIKE_VTYPE
        all_trips.append(trip)
    all_trips.append(EGO_TRIP.copy())

    # Sort by departure time (SUMO requires sorted order)
    all_trips.sort(key=lambda t: float(t.get("depart", 0)))

    root.append(ET.Comment(" Vehicles, persons and containers (sorted by depart) "))
    for attrs in all_trips:
        trip_elem = ET.SubElement(root, "trip")
        for k, v in attrs.items():
            trip_elem.set(k, v)

    ET.indent(root, space="    ")
    ET.ElementTree(root).write(OUTPUT_FILE, encoding="UTF-8", xml_declaration=True)
    print(f"Written: {OUTPUT_FILE}")


if __name__ == "__main__":
    main()

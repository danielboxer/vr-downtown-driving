# Simulation

The simulation extends the Sumo2Unity project. See their documentation [here](https://github.com/SimuTraffX-Lab/SUMO2Unity/blob/main/README.md).

## Scenarios

Six scenarios across two vehicle modes. Select one in the tool before starting Unity.

| Folder           | Vehicle | Description                             |
| ---------------- | ------- | --------------------------------------- |
| calibration_car  | Car     | No traffic, learn controls              |
| downtown_car     | Car     | Free drive                              |
| right_turn_car   | Car     | Right turn at a signalized intersection |
| calibration_bike | Bike    | No traffic, learn controls              |
| downtown_bike    | Bike    | Free ride                               |
| right_turn_bike  | Bike    | Right turn at a signalized intersection |


## Controls

**Car**

| Action             | Keyboard   | VR                           |
| ------------------ | ---------- | ---------------------------- |
| Steer              | A / D      | Tilt controller              |
| Accelerate         | W          | Right trigger                |
| Brake              | S          | Left trigger                 |
| Handbrake          | Space      |                              |
| Left signal        | Q          | Left thumbstick left         |
| Right signal       | E          | Left thumbstick right        |
| Cancel signal      |            | Left thumbstick down         |
| Drive / Reverse    | G (toggle) | Right thumbstick up / down   |
| Calibrate steering | C (hold)   | Left secondary button (hold) |
| Horn               | H          | Right thumbstick click       |


**Bike**

| Action    | Keyboard | VR                     |
| --------- | -------- | ---------------------- |
| Steer     | A / D    | Tilt controller        |
| Brake     | S        | Both triggers (analog) |
| Reverse   | R        | X button               |
| Calibrate | C (hold) | Y button (hold)        |

Speed ramps up automatically when the brakes are released.

## Running

1. Run `uv run main.py` from `simulation/python/`
2. Select a scenario and press **Start simulation**
3. Press Play in Unity

## Driving Evaluation

Scenarios run the driving evaluator, which checks stop-line compliance at red lights and turn signal use at the junction.

## Modifying the SUMO Network

Open `netedit`, then **File > Load Netedit Config** and pick `Sumo2Unity.netecfg` from the scenario folder you want to edit. Remove existing trips or flows before adding new ones.

To renumber edge and junction IDs:

```powershell
netconvert --net-file Sumo2Unity.net.xml -o Sumo2Unity.net.xml `
  --numerical-ids `
  --numerical-ids.edge-start 0 `
  --numerical-ids.node-start 0
```

Save config files after changes:

```powershell
netconvert --net-file Sumo2Unity.net.xml --save-configuration Sumo2Unity.netecfg

sumo --net-file Sumo2Unity.net.xml --save-configuration Sumo2Unity.sumocfg
```

Set the net offset to 0 in the network file; the real-world coordinate offset is too large for Unity.

## Modifying a Route

1. In netedit, switch to **Inspect** mode
2. Click **Demand** in the top bar
3. Click the vehicle of the route you want to change
4. Edit the route parameters

## OpenStreetMap to SUMO

- https://www.youtube.com/watch?v=HQFZKigh4Sk
- https://www.youtube.com/watch?v=Dh_0A-wOk84

```powershell
cd $Env:SUMO_HOME
python .\tools\osmWebWizard.py
```

## VR Setup

https://www.youtube.com/watch?v=exc-73Mna3A

### XR Interaction Simulator

Enable via **Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit > Use XR Interaction Simulator in scenes**.

Right-click rotates the head. Movement keys are rebound to arrow keys to avoid conflict with driving controls.
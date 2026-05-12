# Simulation

The simulation extends the Sumo2Unity project. See their documentation [here](https://github.com/SimuTraffX-Lab/SUMO2Unity/blob/main/README.md).

## Building the Unity project

First install IL2CPP module

1. Open Unity Hub
2. Go to Installs
4. Click the Manage button next to your Unity version
5. Click Add modules
6. Check Windows Build Support (IL2CPP)
7. Click Install

Download Inno Setup which is used for packaging the Unity build into a single file: https://jrsoftware.org/isinfo.php


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

| Action          | Keyboard   | VR                         |
| --------------- | ---------- | -------------------------- |
| Steer           | A / D      | Tilt controller            |
| Accelerate      | W          | Right trigger              |
| Brake           | S          | Left trigger               |
| Handbrake       | Space      |                            |
| Left signal     | 1          | Left thumbstick left       |
| Right signal    | 2          | Left thumbstick right      |
| Cancel signal   | 3          | Left thumbstick down       |
| Drive / Reverse | G (toggle) | Right thumbstick up / down |
| Horn            | H          | Right thumbstick click     |
| Menu            | Escape     |                            |

Steering auto-calibrates when controllers are first detected. Use the in-game menu to re-calibrate manually.


**Bike**

| Action  | Keyboard | VR                     |
| ------- | -------- | ---------------------- |
| Steer   | A / D    | Tilt controller        |
| Brake   | S        | Both triggers (analog) |
| Reverse | R        | X button               |
| Menu    | Escape   |                        |

Speed ramps up automatically after the first trigger press. Squeezing a trigger harder slows the bike; holding a full squeeze while stopped deactivates auto-acceleration (squeeze again to restart).

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
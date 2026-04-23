## Simulation

The simulation uses the Sumo2Unity project. See their documentation [here](https://github.com/SimuTraffX-Lab/SUMO2Unity/blob/main/README.md). 

## Controls

Car:

Steer: A/D keys, right thumbstick X in VR
Accelerate: W key, right trigger in VR
Brake: S key, left trigger in VR
Handbrake: Space
Left signal: 1 key, left primary button (X on Quest) in VR
Right signal: 2 key, right secondary button (B on Quest) in VR
Cancel signal: 3 key, right primary button (A on Quest) in VR
Calibrate steering center: C key, left secondary button (Y on Quest) in VR
Horn: H key, right thumbstick click in VR
Bike:

Steer: A/D keys, right thumbstick X in VR
Brake: S key, left trigger in VR (right trigger also if you add second brake)
Accelerate: automatic, ramps to full throttle when brake released
Calibrate steering center: C key, left secondary button (Y on Quest) in VR

## Running the simulation

1. Run `Sumo2UnityTool.exe`
2. Press `Start simulation`
3. Run Unity simulation

## Modifying the SUMO network

- Open `netedit`
- Click `File` > `Load Netedit Config`
- Select `Sumo2Unity.netecfg`

Make sure to delete any trips or flows in .rou file before making your own trips.


## Modifying a route

1. In netedit, go to `Inspect` mode
2. Click `Demand` in the top bar
3. Click the car of the route you want to edit
4. Now you can modify the route parameters


## OpenStreetMap to Sumo

https://www.youtube.com/watch?v=HQFZKigh4Sk
https://www.youtube.com/watch?v=Dh_0A-wOk84

Run the OSM Web Wizard:
```powershell
cd $Env:SUMO_HOME
python .\tools\osmWebWizard.py
```

Run this to make the edge and junctions ids more readable:

```powershell
netconvert --net-file Sumo2Unity.net.xml -o Sumo2Unity.net.xml `
  --numerical-ids `
  --numerical-ids.edge-start 0 `
  --numerical-ids.node-start 0
```

Run this command to save additional files needed:

```powershell
netconvert --net-file Sumo2Unity.net.xml --save-configuration Sumo2Unity.netecfg

sumo --net-file Sumo2Unity.net.xml --save-configuration Sumo2Unity.sumocfg
```

Finally, in the network file, set the net offset to 0 since it will have a large real world offset.

## VR setup unity

https://www.youtube.com/watch?v=exc-73Mna3A


### Unity XR Interaction Simulator

Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit > Use XR Interaction Simulator in scenes

Use right click to rotate head.
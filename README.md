# VR Downtown Driving

## Simulation

1. Run `Sumo2UnityTool.exe`
2. Press `Start simulation`
3. Run Unity simulation

## Modifying the SUMO network

- Open `netedit`
- Click `File` > `Load Netedit Config`
- Select `Sumo2Unity.netecfg`

Make sure to delete any trips or flows in .rou file before making your own trips.


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
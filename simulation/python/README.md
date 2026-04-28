# Sumo2UnityTool

GUI for selecting and changing scenarios. There is a dropdown that allows you to run different SUMO scenarios.

## Install

```bash
cd simulation/python
uv sync
```

## Run

```bash
uv run main.py
```

## Build Executable

```bash
uv run pyinstaller --onefile --windowed main.py -n Sumo2UnityTool
```
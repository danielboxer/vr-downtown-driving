/// <summary>
/// Centralized list of all scenario identifiers.
/// Used as a dropdown in ScenarioManager, DrivingEvaluator, and any other
/// component that needs to reference a scenario by name.
/// Add new scenarios here — they will automatically appear in Inspector dropdowns.
/// </summary>
public enum ScenarioId
{
    calibration_car,
    downtown_car,
    right_turn_car,
    calibration_bike,
    downtown_bike,
    right_turn_bike
}

/// <summary>
/// Centralized list of all scenario identifiers.
/// Used as a dropdown in ScenarioManager, DrivingEvaluator, and any other
/// component that needs to reference a scenario by name.
/// Add new scenarios here — they will automatically appear in Inspector dropdowns.
/// </summary>
public enum ScenarioId
{
    EgoCar_Calibration,
    EgoCar_Free_Drive,
    EgoCar_Right_Turn,
    EgoBike_Calibration,
    EgoBike_Free_Bike,
    EgoBike_Right_Turn
}

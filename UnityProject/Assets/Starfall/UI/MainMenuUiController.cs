using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuUiController : MonoBehaviour
    {
        private UIDocument document;
        private TextField pilotName;
        private Label empireDescription;
        private string empireId = "aurelian";

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            var root = document.rootVisualElement;
            pilotName = root.Q<TextField>("pilot-name");
            empireDescription = root.Q<Label>("empire-description");
            BindEmpire(root, "empire-aurelian", "aurelian", "Golden laser specialists with resilient armor.");
            BindEmpire(root, "empire-kaldari", "kaldari", "Missile and railgun doctrine backed by massive shields.");
            BindEmpire(root, "empire-meridian", "meridian", "Fast close-range blaster ships built for decisive brawls.");
            BindEmpire(root, "empire-varkhald", "varkhald", "Rugged projectile vessels with unmatched sublight speed.");
            root.Q<Button>("launch")?.RegisterCallback<ClickEvent>(_ =>
                StarfallUiBridge.Host?.StartNewGame(string.IsNullOrWhiteSpace(pilotName?.value) ? "Pilot" : pilotName.value.Trim(), empireId));
            root.Q<Button>("continue")?.RegisterCallback<ClickEvent>(_ => StarfallUiBridge.Host?.ContinueGame());
            root.Q<Button>("import")?.RegisterCallback<ClickEvent>(_ => StarfallUiBridge.Host?.ImportLegacy());
            root.Q<Button>("settings")?.RegisterCallback<ClickEvent>(_ => StarfallUiBridge.Host?.Execute("settings"));
        }

        private void BindEmpire(VisualElement root, string elementName, string id, string description)
        {
            var button = root.Q<Button>(elementName);
            if (button == null) return;
            button.clicked += () =>
            {
                empireId = id;
                root.Query<Button>(className: "empire-card").ForEach(card => card.RemoveFromClassList("chosen"));
                button.AddToClassList("chosen");
                if (empireDescription != null) empireDescription.text = description;
            };
        }
    }
}

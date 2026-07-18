using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    public sealed class ConfirmationOverlay : IDisposable
    {
        private readonly VisualElement overlay;
        private readonly Label title;
        private readonly Label message;
        private readonly Button confirm;
        private readonly Button cancel;
        private Action confirmed;

        public ConfirmationOverlay(VisualElement documentRoot)
        {
            overlay = new VisualElement { name = "mobile-confirmation", pickingMode = PickingMode.Position };
            overlay.AddToClassList("screen");
            overlay.AddToClassList("confirmation-overlay");
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0f, 0.015f, 0.035f, 0.88f);

            var card = new VisualElement { name = "confirmation-card" };
            card.AddToClassList("panel");
            card.AddToClassList("confirmation-card");
            card.style.width = Length.Percent(82f);
            card.style.maxWidth = 620f;
            card.style.paddingLeft = 20f;
            card.style.paddingRight = 20f;
            card.style.paddingTop = 18f;
            card.style.paddingBottom = 18f;

            title = new Label { name = "confirmation-title" };
            title.style.fontSize = 22f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            message = new Label { name = "confirmation-message" };
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.marginTop = 10f;
            message.style.marginBottom = 16f;

            var actions = new VisualElement { name = "confirmation-actions" };
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            cancel = new Button(Close) { name = "confirmation-cancel", text = "CANCEL" };
            confirm = new Button(Confirm) { name = "confirmation-confirm" };
            cancel.style.minWidth = 112f;
            cancel.style.minHeight = 48f;
            confirm.style.minWidth = 112f;
            confirm.style.minHeight = 48f;
            confirm.style.marginLeft = 8f;
            actions.Add(cancel);
            actions.Add(confirm);

            card.Add(title);
            card.Add(message);
            card.Add(actions);
            overlay.Add(card);
            documentRoot.Add(overlay);
            Close();
        }

        public bool IsOpen => overlay.style.display.value == DisplayStyle.Flex;

        public void Show(string heading, string body, string confirmLabel, Action onConfirmed)
        {
            title.text = heading ?? string.Empty;
            message.text = body ?? string.Empty;
            confirm.text = string.IsNullOrWhiteSpace(confirmLabel) ? "CONFIRM" : confirmLabel;
            confirmed = onConfirmed;
            overlay.BringToFront();
            overlay.style.display = DisplayStyle.Flex;
            cancel.Focus();
        }

        public void Close()
        {
            confirmed = null;
            overlay.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            confirmed = null;
            overlay.RemoveFromHierarchy();
        }

        private void Confirm()
        {
            var action = confirmed;
            Close();
            action?.Invoke();
        }
    }
}

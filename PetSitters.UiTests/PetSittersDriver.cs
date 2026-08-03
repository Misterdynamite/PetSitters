using System;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace PetSitters.UiTests
{
    /// <summary>
    /// A small, readable wrapper around FlaUI that speaks in terms of the
    /// PetSitters UI (text boxes, buttons, tabs, lists) instead of raw UI
    /// Automation calls. Every lookup retries for a few seconds so the tests
    /// tolerate the app still rendering a freshly-swapped view.
    ///
    /// Controls are found by their WPF <c>x:Name</c>, which WPF exposes to UI
    /// Automation as the AutomationId - so the selectors below match the names in
    /// the XAML (EmailBox, PasswordBox, RateBox, ...). Buttons and tabs, which
    /// have no x:Name, are found by their visible text.
    /// </summary>
    internal sealed class PetSittersDriver : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly Application _app;
        private readonly UIA3Automation _automation;
        private readonly Window _window;

        /// <summary>
        /// A pause added after every input/click/tab action so the run is easy to
        /// follow by eye. Set to <see cref="TimeSpan.Zero"/> for a fast, silent run.
        /// </summary>
        public TimeSpan ActionDelay { get; set; } = TimeSpan.FromMilliseconds(700);

        /// <summary>Launches PetSitters.exe and waits for its main window.</summary>
        public PetSittersDriver(string executablePath)
        {
            _automation = new UIA3Automation();
            _app = Application.Launch(executablePath);

            _window = Retry.WhileNull(
                () => _app.GetMainWindow(_automation),
                Timeout).Result
                ?? throw new InvalidOperationException("The PetSitters main window did not appear within " + Timeout.TotalSeconds + "s.");
        }

        // ---- element lookup --------------------------------------------------

        /// <summary>Finds a control by its <c>x:Name</c> / AutomationId, retrying until it appears.</summary>
        public AutomationElement ByName(string automationId)
        {
            return Retry.WhileNull(
                () => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
                Timeout).Result
                ?? throw new InvalidOperationException("No control with AutomationId '" + automationId + "' was found.");
        }

        /// <summary>True if a control with the given <c>x:Name</c> is present within a short wait.</summary>
        public bool Exists(string automationId)
        {
            return Retry.WhileNull(
                () => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
                TimeSpan.FromSeconds(4)).Result != null;
        }

        /// <summary>True if any element anywhere in the window renders the given text (label, list cell, ...).</summary>
        public bool HasText(string text)
        {
            return Retry.WhileNull(
                () => _window.FindFirstDescendant(cf => cf.ByName(text)),
                TimeSpan.FromSeconds(6)).Result != null;
        }

        // ---- input -----------------------------------------------------------

        /// <summary>Clears a normal text box and types the given value (simulated keystrokes).</summary>
        public void EnterText(string automationId, string value)
        {
            ByName(automationId).AsTextBox().Enter(value);
            Pause();
        }

        /// <summary>
        /// Types into a WPF PasswordBox. Password boxes intentionally block
        /// programmatic value-setting through UI Automation, so we focus the box
        /// and send real keystrokes instead.
        /// </summary>
        public void EnterPassword(string automationId, string value)
        {
            AutomationElement box = ByName(automationId);
            box.Focus();
            Keyboard.Type(value);
            Pause();
        }

        /// <summary>Selects a radio button (e.g. the Owner/Sitter role choice).</summary>
        public void SelectRadio(string automationId)
        {
            ByName(automationId).AsRadioButton().IsChecked = true;
            Pause();
        }

        /// <summary>Selects a combo-box item by its displayed text.</summary>
        public void SelectComboItem(string automationId, string itemText)
        {
            ByName(automationId).AsComboBox().Select(itemText);
            Pause();
        }

        /// <summary>Selects the first row of a list/list-view by its <c>x:Name</c>.</summary>
        public void SelectFirstListItem(string automationId)
        {
            ListBox list = ByName(automationId).AsListBox();
            ListBoxItem[] items = Retry.WhileEmpty(() => list.Items, Timeout).Result;
            items[0].Select();
            Pause();
        }

        // ---- clicking --------------------------------------------------------

        /// <summary>Clicks a button identified by its visible text (e.g. "Create account").</summary>
        public void ClickButton(string buttonText)
        {
            AutomationElement element = Retry.WhileNull(
                () => _window.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName(buttonText))),
                Timeout).Result
                ?? throw new InvalidOperationException("No button labelled '" + buttonText + "' was found.");

            element.AsButton().Invoke();
            Pause();
        }

        /// <summary>Clicks a button identified by its <c>x:Name</c> (e.g. the Log out button).</summary>
        public void ClickButtonById(string automationId)
        {
            ByName(automationId).AsButton().Invoke();
            Pause();
        }

        /// <summary>Switches to a dashboard tab by its header text (e.g. "My Pets").</summary>
        public void SelectTab(string header)
        {
            Tab tab = Retry.WhileNull(
                () => _window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tab)),
                Timeout).Result?.AsTab()
                ?? throw new InvalidOperationException("No tab strip was found on the current view.");

            tab.SelectTabItem(header);
            Pause();
        }

        // ---- reading ---------------------------------------------------------

        /// <summary>Reads the text of a label/status control by its <c>x:Name</c>.</summary>
        public string ReadText(string automationId)
        {
            return ByName(automationId).Name;
        }

        // ---- dialogs ---------------------------------------------------------

        /// <summary>Waits for a dialog window whose title contains the given text.</summary>
        private AutomationElement WaitForWindow(string titleContains)
        {
            return Retry.WhileNull<AutomationElement>(
                () => FindOpenWindow(titleContains),
                Timeout).Result
                ?? throw new InvalidOperationException("No window titled like '" + titleContains + "' appeared.");
        }

        /// <summary>
        /// Finds an open window by (partial) title, or null. A modal
        /// <c>ShowDialog</c> window is a UI Automation child of its owner window,
        /// so we check the owner's modal children first, then fall back to the
        /// process's top-level (desktop-owned) windows.
        /// </summary>
        private AutomationElement FindOpenWindow(string titleContains)
        {
            foreach (Window modal in _window.ModalWindows)
            {
                if (TitleMatches(modal, titleContains))
                    return modal;
            }

            foreach (Window window in _app.GetAllTopLevelWindows(_automation))
            {
                if (TitleMatches(window, titleContains))
                    return window;
            }

            return null;
        }

        private static bool TitleMatches(Window window, string titleContains)
        {
            return !string.IsNullOrEmpty(window.Title) &&
                   window.Title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>True if a dialog (matched by title) contains an element rendering the given text.</summary>
        public bool DialogHasText(string titleContains, string text)
        {
            AutomationElement dialog = WaitForWindow(titleContains);
            return Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByName(text)),
                TimeSpan.FromSeconds(4)).Result != null;
        }

        /// <summary>Clicks a button (by its visible text) inside a dialog matched by title.</summary>
        public void ClickDialogButton(string titleContains, string buttonText)
        {
            AutomationElement dialog = WaitForWindow(titleContains);
            AutomationElement button = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName(buttonText))),
                Timeout).Result
                ?? throw new InvalidOperationException("No button labelled '" + buttonText + "' inside dialog '" + titleContains + "'.");

            button.AsButton().Invoke();
            Pause();
        }

        /// <summary>Waits <see cref="ActionDelay"/> so the run is easy to watch.</summary>
        private void Pause()
        {
            if (ActionDelay > TimeSpan.Zero)
                Thread.Sleep(ActionDelay);
        }

        public void Dispose()
        {
            // Close any dialog still open (e.g. if a test failed mid-popup) so the
            // main window isn't blocked from closing.
            try
            {
                if (_app != null && !_app.HasExited)
                {
                    foreach (Window modal in _window.ModalWindows)
                    {
                        try { modal.Close(); } catch { /* best-effort */ }
                    }
                }
            }
            catch
            {
                /* best-effort */
            }

            try
            {
                if (_app != null && !_app.HasExited)
                    _app.Close();
            }
            catch
            {
                /* best-effort close */
            }

            try
            {
                if (_app != null && !_app.HasExited)
                    _app.Kill();
            }
            catch
            {
                /* best-effort kill */
            }

            _automation?.Dispose();
        }
    }
}

using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLikerUI
{
    /// <summary>
    /// Which AI the AI button of the text uses: the language model on this computer, or an AI service (ChatGPT, Gemini, Claude) the person gave a key for. The box lists the model on this computer and
    /// the services that have a key; the AI services button opens the window where the keys are set. What is picked is kept (<see cref="AiServiceSettings"/>), for the next page and the next run.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        private readonly AiServiceSettings _ai;
        private bool _fillingAi;

        /// <summary>The keys of the AI services and which one is picked (for the tests).</summary>
        internal AiServiceSettings AiSettings => _ai;

        /// <summary>Stands in for the Internet when an AI service is asked (a test, which must not call a real service); null is the real one.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal HttpMessageHandler? CloudHandler { get; set; }

        /// <summary>Stands in for the AI services window (a test, which can not answer a window): gets the settings, may change them, and says what the window would have returned.</summary>
        [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Func<AiServiceSettings, DialogResult>? ShowAiServices { get; set; }

        /// <summary>What asks the AI services: the client of the program, or the one of a test.</summary>
        private HttpClient CloudClient => CloudHandler is { } handler ? CloudHttp.Create(handler) : CloudHttp.Shared;

        /// <summary>The service that is picked in the box, null for the model on this computer.</summary>
        internal AiVendor? PickedAi => AiCombo.SelectedItem is Choice<AiVendor?> choice ? choice.Value : null;

        /// <summary>Fills the box: the model on this computer, and each service that has a key, with the model it will be asked; the one that is picked stays picked.</summary>
        internal void FillAiCombo()
        {
            _fillingAi = true;
            try
            {
                AiCombo.Items.Clear();
                AiCombo.Items.Add(new Choice<AiVendor?>(null, ManuscriptStrings.AiLocal));
                foreach (var vendor in AiVendors.All)
                {
                    if (_ai.Get(vendor) is { } account)
                        AiCombo.Items.Add(new Choice<AiVendor?>(vendor, $"{AiVendors.Name(vendor)} - {account.Model}"));
                }

                var picked = _ai.Active?.Vendor;
                var index = picked is null ? 0 : AiCombo.Items.Cast<Choice<AiVendor?>>().ToList().FindIndex(choice => choice.Value == picked);
                AiCombo.SelectedIndex = Math.Max(0, index);
            }
            finally
            {
                _fillingAi = false;
            }
        }

        private void OnAiPicked()
        {
            if (_fillingAi || AiCombo.SelectedItem is not Choice<AiVendor?> choice)
                return;

            _ai.Engine = choice.Value;
            SaveAiSettings();
            UpdateButtons(); // the context of the pages is for a service
        }

        private void SaveAiSettings()
        {
            try
            {
                _ai.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(AiServicesStrings.SaveFailedFormat, ex.Message));
            }
        }

        /// <summary>The AI services button: the window of the keys. A person who had no service and gives a key for one has come to use it, so it is picked.</summary>
        internal void OpenAiServices()
        {
            if (_busy)
                return;

            var hadService = AiVendors.All.Any(_ai.HasKey);
            var result = ShowAiServices is { } show ? show(_ai) : ShowRealAiServices();
            if (result != DialogResult.OK || IsDisposed)
                return;

            if (!hadService)
            {
                foreach (var vendor in AiVendors.All)
                {
                    if (!_ai.HasKey(vendor))
                        continue;

                    _ai.Engine = vendor;
                    SaveAiSettings();
                    break;
                }
            }

            FillAiCombo();
            UpdateButtons();
        }

        private DialogResult ShowRealAiServices()
        {
            using var dialog = new AiServicesDialog(_ai, _dark, CloudClient);
            return dialog.ShowDialog(this);
        }
    }
}

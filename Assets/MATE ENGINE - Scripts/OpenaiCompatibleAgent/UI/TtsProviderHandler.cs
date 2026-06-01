using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Populates a TMP_Dropdown with the available TTS providers, persists the selection
    /// via SaveLoadHandler, and seeds StreamingOrchestrator.CurrentProvider on startup.
    /// Mirrors VoiceCatalogHandler. Attach to the provider dropdown GameObject and assign
    /// the dropdown + orchestrator in the Inspector.
    /// </summary>
    public class TtsProviderHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Dropdown dropdown;
        [SerializeField] private StreamingOrchestrator orchestrator;

        // Index order MUST stay aligned with _labels.
        private static readonly TtsProvider[] _providers = { TtsProvider.FishSpeech, TtsProvider.Irodori };
        private static readonly string[] _labels = { "Fish-Speech (8092)", "Irodori (8091)" };

        private void Start()
        {
            if (dropdown == null)
            {
                Debug.LogError("[TtsProvider] Dropdown is not assigned in Inspector.");
                return;
            }
            StartCoroutine(InitializeWhenReady());
        }

        private IEnumerator InitializeWhenReady()
        {
            // SaveLoadHandler.Instance may not exist until after its Awake.
            yield return new WaitUntil(() => SaveLoadHandler.Instance != null);
            PopulateDropdown();
            RestoreSelection();
            SeedOrchestrator();
        }

        private void PopulateDropdown()
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(_labels));
            dropdown.onValueChanged.AddListener(OnDropdownChanged);
        }

        private void RestoreSelection()
        {
            TtsProvider saved = StreamingOrchestrator.ParseProvider(SaveLoadHandler.Instance.data.ttsProvider);
            int index = Array.IndexOf(_providers, saved);
            if (index < 0) index = 0;
            dropdown.SetValueWithoutNotify(index);
        }

        private void SeedOrchestrator()
        {
            if (orchestrator == null)
            {
                Debug.LogWarning("[TtsProvider] StreamingOrchestrator is not assigned — provider will not take effect until wired.");
                return;
            }
            orchestrator.CurrentProvider = _providers[dropdown.value];
            Debug.Log($"[TtsProvider] Seeded orchestrator with provider: {_providers[dropdown.value]}");
        }

        private void OnDropdownChanged(int index)
        {
            if (index < 0 || index >= _providers.Length) return;
            TtsProvider provider = _providers[index];

            if (orchestrator != null) orchestrator.CurrentProvider = provider;

            SaveLoadHandler.Instance.data.ttsProvider = provider.ToString();
            SaveLoadHandler.Instance.SaveToDisk();

            Debug.Log($"[TtsProvider] Provider changed to: {provider}");
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Populates a TMP_Dropdown with available voice ids from the Irodori voices
    /// directory, persists the selection via SaveLoadHandler, and seeds
    /// StreamingOrchestrator.CurrentVoiceId on startup.
    /// </summary>
    public class VoiceCatalogHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Dropdown dropdown;
        [SerializeField] private StreamingOrchestrator orchestrator;
        [SerializeField] private IrodoriClient irodoriClient;

        private List<string> _voiceIds;

        private void Start()
        {
            if (dropdown == null)
            {
                Debug.LogError("[Voice] Dropdown is not assigned in Inspector.");
                return;
            }

            if (irodoriClient == null)
            {
                Debug.LogError("[Voice] IrodoriClient is not assigned in Inspector.");
                return;
            }

            StartCoroutine(InitializeWhenReady());
        }

        private IEnumerator InitializeWhenReady()
        {
            // SaveLoadHandler.Instance may not be available until after its Awake.
            yield return new WaitUntil(() => SaveLoadHandler.Instance != null);

            PopulateDropdown();
            RestoreSelection();
            SeedOrchestrator();
        }

        private void PopulateDropdown()
        {
            string voicesRoot = irodoriClient.VoicesRootPath;

            if (string.IsNullOrEmpty(voicesRoot) || !Directory.Exists(voicesRoot))
            {
                Debug.LogWarning($"[Voice] Voices root path does not exist: '{voicesRoot}'");
                dropdown.interactable = false;
                return;
            }

            var directories = Directory.GetDirectories(voicesRoot);
            _voiceIds = directories
                .Select(p => Path.GetFileName(p))
                .Where(name => File.Exists(Path.Combine(voicesRoot, name, "merged_audio.mp3")))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (_voiceIds.Count == 0)
            {
                Debug.LogWarning("[Voice] No voice folders with merged_audio.mp3 found.");
                dropdown.interactable = false;
                return;
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(_voiceIds);
            dropdown.onValueChanged.AddListener(OnDropdownChanged);

            Debug.Log($"[Voice] Loaded {_voiceIds.Count} voice(s): {string.Join(", ", _voiceIds)}");
        }

        private void RestoreSelection()
        {
            string saved = SaveLoadHandler.Instance.data.selectedVoiceId;
            int index = -1;

            if (!string.IsNullOrEmpty(saved))
                index = _voiceIds.IndexOf(saved);

            // Fallback to IrodoriClient.DefaultVoiceId if saved value not found
            if (index < 0 && !string.IsNullOrEmpty(irodoriClient.DefaultVoiceId))
                index = _voiceIds.IndexOf(irodoriClient.DefaultVoiceId);

            // Final fallback: first entry
            if (index < 0)
                index = 0;

            dropdown.SetValueWithoutNotify(index);
        }

        private void SeedOrchestrator()
        {
            if (orchestrator == null)
            {
                Debug.LogWarning("[Voice] StreamingOrchestrator is not assigned — voice will not take effect until wired.");
                return;
            }

            string selectedId = GetSelectedVoiceId();
            orchestrator.CurrentVoiceId = selectedId;
            Debug.Log($"[Voice] Seeded orchestrator with voice: {selectedId}");
        }

        private void OnDropdownChanged(int index)
        {
            if (_voiceIds == null || index < 0 || index >= _voiceIds.Count)
                return;

            string selectedId = _voiceIds[index];

            if (orchestrator != null)
                orchestrator.CurrentVoiceId = selectedId;

            SaveLoadHandler.Instance.data.selectedVoiceId = selectedId;
            SaveLoadHandler.Instance.SaveToDisk();

            Debug.Log($"[Voice] Voice changed to: {selectedId}");
        }

        private string GetSelectedVoiceId()
        {
            if (_voiceIds == null || _voiceIds.Count == 0)
                return irodoriClient.DefaultVoiceId;

            int index = dropdown.value;
            if (index >= 0 && index < _voiceIds.Count)
                return _voiceIds[index];

            return _voiceIds[0];
        }
    }
}
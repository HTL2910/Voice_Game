using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Utils;
using Button = UnityEngine.UI.Button;
using Toggle = UnityEngine.UI.Toggle;

namespace Whisper.Samples
{
    /// <summary>
    /// Fully automatic voice recording and transcription using VAD.
    /// Automatically records when voice is detected, transcribes, then returns to recording mode.
    /// </summary>
    public class MicrophoneDemo : MonoBehaviour
    {
        public WhisperManager whisper;
        public MicrophoneRecord microphoneRecord;
        public bool streamSegments = true;
        public bool printLanguage = true;

        [Header("VAD Settings")]
        [Tooltip("Minimum recording duration in seconds (prevents very short recordings)")]
        public float minRecordingDuration = 1.0f;
        [Tooltip("Delay before starting to listen again after transcription")]
        public float delayBeforeNextRecord = 0.5f;

        [Header("Action Buttons")]
        public Button okButton;
        public Button hiButton;
        public Button newButton;
        public Button colorButton;
        
        [Header("UI")] 
        public Text outputText;
        public Text timeText;
        public TextMeshProUGUI vadStatusText;
        public TextMeshProUGUI performanceText;
        public Dropdown languageDropdown;
        public Toggle translateToggle;
        public ScrollRect scroll;
        
        private string _buffer;
        private float _recordingStartTime;
        private bool _isProcessing;
        private bool _isReadyForNextRecord;
        
        // Performance tracking
        private int _totalRecordings = 0;
        private float _totalProcessingTime = 0f;
        private float _totalAudioLength = 0f;
        private float _lastFpsUpdate = 0f;
        private int _frameCount = 0;
        private float _currentFps = 0f;
        
        // Store original button colors
        private Color _originalOkColor;
        private Color _originalHiColor;
        private Color _originalNewColor;
        private Color _originalColorColor;
        
        // App lifecycle tracking
        private bool _isAppPaused = false;
        private bool _isAppFocused = true;

        private void Awake()
        {
            whisper.OnNewSegment += OnNewSegment;
            whisper.OnProgress += OnProgressHandler;
            
            microphoneRecord.OnRecordStop += OnRecordStop;
            microphoneRecord.OnVadChanged += OnVadChanged;
            
            languageDropdown.value = languageDropdown.options
                .FindIndex(op => op.text == whisper.language);
            languageDropdown.onValueChanged.AddListener(OnLanguageChanged);

            translateToggle.isOn = whisper.translateToEnglish;
            translateToggle.onValueChanged.AddListener(OnTranslateChanged);

            // Enable VAD features and disable manual stop
            microphoneRecord.useVad = true;
            microphoneRecord.vadStop = false;
            
            // Adjust VAD settings for better sensitivity
            microphoneRecord.vadThd = 0.5f; // Lower threshold for more sensitivity
            microphoneRecord.vadFreqThd = 50.0f; // Lower frequency threshold
            microphoneRecord.vadUpdateRateSec = 0.05f; // Update more frequently
            
            // Check microphone devices
            var devices = Microphone.devices;
            UnityEngine.Debug.Log($"Available microphones: {string.Join(", ", devices)}");
            if (devices.Length == 0)
            {
                UnityEngine.Debug.LogError("No microphone devices found!");
                UpdateVadStatus("No microphone found!", Color.red);
                return;
            }
            
            // Store original button colors
            if (okButton != null) _originalOkColor = okButton.image.color;
            if (hiButton != null) _originalHiColor = hiButton.image.color;
            if (newButton != null) _originalNewColor = newButton.image.color;
            if (colorButton != null) _originalColorColor = colorButton.image.color;
            
            // Start listening immediately
            _isReadyForNextRecord = true;
            UpdateVadStatus("Ready to listen...", Color.blue);
            
            // Debug: Start microphone immediately to enable VAD
            microphoneRecord.StartRecord();
            UnityEngine.Debug.Log("Microphone started for VAD detection");
        }

        private void Update()
        {
            // Update FPS counter
            _frameCount++;
            if (Time.time - _lastFpsUpdate >= 1.0f)
            {
                _currentFps = _frameCount / (Time.time - _lastFpsUpdate);
                _frameCount = 0;
                _lastFpsUpdate = Time.time;
                UpdatePerformanceDisplay();
            }
        }

        private void UpdatePerformanceDisplay()
        {
            if (performanceText == null) return;

            var memoryUsage = System.GC.GetTotalMemory(false) / (1024f * 1024f); // MB
            var avgProcessingTime = _totalRecordings > 0 ? _totalProcessingTime / _totalRecordings : 0f;
            var avgAudioLength = _totalRecordings > 0 ? _totalAudioLength / _totalRecordings : 0f;
            var processingRate = _totalAudioLength > 0 ? _totalAudioLength / _totalProcessingTime : 0f;

            var performanceInfo = $"Performance:\n" +
                                 $"FPS: {_currentFps:F1}\n" +
                                 $"Memory: {memoryUsage:F1} MB\n" +
                                 $"Total Recordings: {_totalRecordings}\n" +
                                 $"Avg Processing: {avgProcessingTime:F0} ms\n" +
                                 $"Avg Audio Length: {avgAudioLength:F1}s\n" +
                                 $"Processing Rate: {processingRate:F1}x";

            performanceText.text = performanceInfo;
        }

        private void OnVadChanged(bool isVoiceDetected)
        {
            UnityEngine.Debug.Log($"VAD Changed: {isVoiceDetected}, IsRecording: {microphoneRecord.IsRecording}, IsProcessing: {_isProcessing}, IsReady: {_isReadyForNextRecord}");
            
            // Don't process VAD if app is paused or not focused
            if (_isAppPaused || !_isAppFocused)
            {
                UnityEngine.Debug.Log($"VAD ignored - App paused: {_isAppPaused}, App focused: {_isAppFocused}");
                return;
            }
            
            // Don't process if we're currently processing a previous recording
            if (_isProcessing) return;

            if (isVoiceDetected && !microphoneRecord.IsRecording && _isReadyForNextRecord)
            {
                // Voice detected - start recording
                UnityEngine.Debug.Log("Voice detected - starting recording");
                StartRecording();
            }
            else if (!isVoiceDetected && microphoneRecord.IsRecording)
            {
                // Silence detected - check if we should stop recording
                var recordingDuration = Time.time - _recordingStartTime;
                UnityEngine.Debug.Log($"Silence detected - recording duration: {recordingDuration:F2}s");
                if (recordingDuration >= minRecordingDuration)
                {
                    UnityEngine.Debug.Log("Stopping recording due to silence");
                    StopRecording();
                }
            }
        }

        private void StartRecording()
        {
            if (microphoneRecord.IsRecording) return;
            
            _recordingStartTime = Time.time;
            microphoneRecord.StartRecord();
            UpdateVadStatus("Recording...", Color.green);
        }

        private void StopRecording()
        {
            if (!microphoneRecord.IsRecording) return;
            
            _isProcessing = true;
            microphoneRecord.StopRecord();
            UpdateVadStatus("Processing...", Color.yellow);
        }

        private void UpdateVadStatus(string status, Color color)
        {
            if (vadStatusText != null)
            {
                vadStatusText.text = status;
                vadStatusText.color = color;
            }
        }
        
        private void ProcessWhisperResult(string text)
        {
            // Reset all button colors to original
            ResetButtonColors();
            
            // Convert to lowercase for case-insensitive matching
            string lowerText = text.ToLower();
            
            // Check for keywords and change button colors
            if (lowerText.Contains("ok") || lowerText.Contains("okay"))
            {
                if (okButton != null)
                {
                    okButton.image.color = Color.green;
                    UnityEngine.Debug.Log("OK detected - button turned green");
                }
            }
            
            if (lowerText.Contains("hi") || lowerText.Contains("hello") || lowerText.Contains("hey"))
            {
                if (hiButton != null)
                {
                    hiButton.image.color = Color.blue;
                    UnityEngine.Debug.Log("HI detected - button turned blue");
                }
            }
            
            if (lowerText.Contains("new"))
            {
                if (newButton != null)
                {
                    newButton.image.color = Color.yellow;
                    UnityEngine.Debug.Log("NEW detected - button turned yellow");
                }
            }
            
            // Enhanced color detection for color button
            Color detectedColor = DetectColorFromText(lowerText);
            if (detectedColor != Color.clear && colorButton != null)
            {
                colorButton.image.color = detectedColor;
                UnityEngine.Debug.Log($"Color detected: {detectedColor} - color button changed");
            }
        }
        
        private Color DetectColorFromText(string text)
        {
            // Dictionary of color names and their corresponding Unity Colors
            var colorMap = new System.Collections.Generic.Dictionary<string, Color>
            {
                {"red", Color.red},
                {"blue", Color.blue},
                {"green", Color.green},
                {"yellow", Color.yellow},
                {"orange", new Color(1f, 0.5f, 0f)}, // Orange
                {"purple", new Color(0.5f, 0f, 0.5f)}, // Purple
                {"pink", Color.magenta},
                {"magenta", Color.magenta},
                {"cyan", Color.cyan},
                {"white", Color.white},
                {"black", Color.black},
                {"gray", Color.gray},
                {"grey", Color.gray},
                {"brown", new Color(0.6f, 0.4f, 0.2f)}, // Brown
                {"lime", new Color(0f, 1f, 0f)}, // Lime green
                {"navy", new Color(0f, 0f, 0.5f)}, // Navy blue
                {"teal", new Color(0f, 0.5f, 0.5f)}, // Teal
                {"maroon", new Color(0.5f, 0f, 0f)}, // Maroon
                {"olive", new Color(0.5f, 0.5f, 0f)}, // Olive
                {"violet", new Color(0.5f, 0f, 1f)}, // Violet
                {"indigo", new Color(0.3f, 0f, 0.5f)}, // Indigo
                {"turquoise", new Color(0f, 0.8f, 0.8f)}, // Turquoise
                {"gold", new Color(1f, 0.8f, 0f)}, // Gold
                {"silver", new Color(0.8f, 0.8f, 0.8f)}, // Silver
                {"salmon", new Color(1f, 0.6f, 0.6f)}, // Salmon
                {"lavender", new Color(0.9f, 0.9f, 1f)}, // Lavender
                {"plum", new Color(0.8f, 0.2f, 0.8f)}, // Plum
                {"khaki", new Color(0.8f, 0.8f, 0.6f)}, // Khaki
                {"beige", new Color(0.9f, 0.9f, 0.8f)}, // Beige
                {"mint", new Color(0.8f, 1f, 0.8f)}, // Mint
                {"peach", new Color(1f, 0.8f, 0.6f)}, // Peach
                {"rose", new Color(1f, 0.4f, 0.6f)}, // Rose
                {"azure", new Color(0f, 0.5f, 1f)}, // Azure
                {"crimson", new Color(0.9f, 0f, 0.2f)}, // Crimson
                {"emerald", new Color(0f, 0.8f, 0.4f)}, // Emerald
                {"sapphire", new Color(0f, 0.3f, 0.8f)}, // Sapphire
                {"ruby", new Color(0.8f, 0f, 0.2f)}, // Ruby
                {"amber", new Color(1f, 0.7f, 0f)}, // Amber
                {"jade", new Color(0f, 0.6f, 0.4f)}, // Jade
                {"copper", new Color(0.8f, 0.5f, 0.2f)}, // Copper
                {"bronze", new Color(0.8f, 0.5f, 0.2f)}, // Bronze
                {"charcoal", new Color(0.2f, 0.2f, 0.2f)}, // Charcoal
                {"ivory", new Color(1f, 1f, 0.9f)}, // Ivory
                {"cream", new Color(1f, 0.9f, 0.8f)}, // Cream
                {"fuchsia", new Color(1f, 0f, 1f)}, // Fuchsia
                {"lime green", new Color(0f, 1f, 0f)}, // Lime green
                {"light blue", new Color(0.5f, 0.8f, 1f)}, // Light blue
                {"dark blue", new Color(0f, 0f, 0.5f)}, // Dark blue
                {"light green", new Color(0.5f, 1f, 0.5f)}, // Light green
                {"dark green", new Color(0f, 0.4f, 0f)}, // Dark green
                {"light red", new Color(1f, 0.5f, 0.5f)}, // Light red
                {"dark red", new Color(0.5f, 0f, 0f)}, // Dark red
                {"light yellow", new Color(1f, 1f, 0.5f)}, // Light yellow
                {"dark yellow", new Color(0.8f, 0.8f, 0f)}, // Dark yellow
                {"light purple", new Color(0.8f, 0.5f, 0.8f)}, // Light purple
                {"dark purple", new Color(0.3f, 0f, 0.3f)}, // Dark purple
                {"light pink", new Color(1f, 0.8f, 0.8f)}, // Light pink
                {"dark pink", new Color(0.8f, 0.2f, 0.4f)}, // Dark pink
                {"light gray", new Color(0.8f, 0.8f, 0.8f)}, // Light gray
                {"dark gray", new Color(0.3f, 0.3f, 0.3f)}, // Dark gray
                {"light brown", new Color(0.8f, 0.6f, 0.4f)}, // Light brown
                {"dark brown", new Color(0.4f, 0.2f, 0f)}, // Dark brown
            };
            
            // Check for color keywords in the text
            foreach (var colorEntry in colorMap)
            {
                if (text.Contains(colorEntry.Key))
                {
                    return colorEntry.Value;
                }
            }
            
            // If no specific color found, return clear (no change)
            return Color.clear;
        }
        
        private void ResetButtonColors()
        {
            if (okButton != null) okButton.image.color = _originalOkColor;
            if (hiButton != null) hiButton.image.color = _originalHiColor;
            if (newButton != null) newButton.image.color = _originalNewColor;
            if (colorButton != null) colorButton.image.color = _originalColorColor;
        }
        
        private async void OnRecordStop(AudioChunk recordedAudio)
        {
            // Only process if we have meaningful audio
            if (recordedAudio.Data.Length == 0)
            {
                _isProcessing = false;
                _isReadyForNextRecord = true;
                UpdateVadStatus("Ready to listen...", Color.blue);
                // Restart microphone for VAD detection
                microphoneRecord.StartRecord();
                UnityEngine.Debug.Log("Restarted microphone after empty recording");
                return;
            }

            var sw = new Stopwatch();
            sw.Start();
            
            var res = await whisper.GetTextAsync(recordedAudio.Data, recordedAudio.Frequency, recordedAudio.Channels);
            
            var time = sw.ElapsedMilliseconds;
            var rate = recordedAudio.Length / (time * 0.001f);
            
            // Update performance statistics
            _totalRecordings++;
            _totalProcessingTime += time;
            _totalAudioLength += recordedAudio.Length;
            
            if (timeText != null)
                timeText.text = $"Time: {time} ms\nRate: {rate:F1}x";

            if (res != null && outputText != null)
            {
                var text = res.Result;
                if (printLanguage)
                    text += $"\n\nLanguage: {res.Language}";
                
                outputText.text = text;
                UiUtils.ScrollDown(scroll);
                
                // Process the result for button actions
                ProcessWhisperResult(res.Result);
            }

            // Wait a bit before listening again
            await System.Threading.Tasks.Task.Delay((int)(delayBeforeNextRecord * 1000));
            
            _isProcessing = false;
            _isReadyForNextRecord = true;
            UpdateVadStatus("Ready to listen...", Color.blue);
            
            // Restart microphone for VAD detection
            microphoneRecord.StartRecord();
            UnityEngine.Debug.Log("Restarted microphone after processing");
        }
        
        private void OnLanguageChanged(int ind)
        {
            var opt = languageDropdown.options[ind];
            whisper.language = opt.text;
        }
        
        private void OnTranslateChanged(bool translate)
        {
            whisper.translateToEnglish = translate;
        }

        private void OnProgressHandler(int progress)
        {
            if (!timeText)
                return;
            timeText.text = $"Progress: {progress}%";
        }
        
        private void OnNewSegment(WhisperSegment segment)
        {
            if (!streamSegments || !outputText)
                return;

            _buffer += segment.Text;
            outputText.text = _buffer + "...";
            UiUtils.ScrollDown(scroll);
        }
        
        // App lifecycle management
        private void OnApplicationPause(bool pauseStatus)
        {
            _isAppPaused = pauseStatus;
            UnityEngine.Debug.Log($"App paused: {pauseStatus}");
            
            if (pauseStatus)
            {
                // App is being paused - stop VAD and recording
                if (microphoneRecord.IsRecording)
                {
                    UnityEngine.Debug.Log("Stopping recording due to app pause");
                    microphoneRecord.StopRecord();
                }
                UpdateVadStatus("App paused - VAD stopped", Color.gray);
            }
            else
            {
                // App is being resumed - restart VAD if conditions are met
                if (_isAppFocused && !_isProcessing && _isReadyForNextRecord)
                {
                    UnityEngine.Debug.Log("Restarting VAD after app resume");
                    microphoneRecord.StartRecord();
                    UpdateVadStatus("Ready to listen...", Color.blue);
                }
            }
        }
        
        private void OnApplicationFocus(bool hasFocus)
        {
            _isAppFocused = hasFocus;
            UnityEngine.Debug.Log($"App focused: {hasFocus}");
            
            if (!hasFocus)
            {
                // App lost focus - stop VAD and recording
                if (microphoneRecord.IsRecording)
                {
                    UnityEngine.Debug.Log("Stopping recording due to app focus loss");
                    microphoneRecord.StopRecord();
                }
                UpdateVadStatus("App lost focus - VAD stopped", Color.gray);
            }
            else
            {
                // App gained focus - restart VAD if conditions are met
                if (!_isAppPaused && !_isProcessing && _isReadyForNextRecord)
                {
                    UnityEngine.Debug.Log("Restarting VAD after app focus gain");
                    microphoneRecord.StartRecord();
                    UpdateVadStatus("Ready to listen...", Color.blue);
                }
            }
        }
        
        private void OnDestroy()
        {
            UnityEngine.Debug.Log("MicrophoneDemo being destroyed - cleaning up resources");
            
            // Unsubscribe from events to prevent memory leaks
            if (whisper != null)
            {
                whisper.OnNewSegment -= OnNewSegment;
                whisper.OnProgress -= OnProgressHandler;
            }
            
            if (microphoneRecord != null)
            {
                microphoneRecord.OnRecordStop -= OnRecordStop;
                microphoneRecord.OnVadChanged -= OnVadChanged;
                
                // Stop recording if active
                if (microphoneRecord.IsRecording)
                {
                    microphoneRecord.StopRecord();
                }
            }
            
            // Unsubscribe from UI events
            if (languageDropdown != null)
            {
                languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            }
            
            if (translateToggle != null)
            {
                translateToggle.onValueChanged.RemoveListener(OnTranslateChanged);
            }
        }
    }
}
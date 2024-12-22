using System.Timers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace HITit
{
#if ANDROID
    // Alarm finishes => Next interval
    public class AlarmCompletionListener
        : Java.Lang.Object, Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly Action _onCompletion;
        public AlarmCompletionListener(Action onCompletion)
        {
            _onCompletion = onCompletion;
        }
        public void OnCompletion(Android.Media.MediaPlayer mp)
        {
            _onCompletion?.Invoke();
        }
    }

    // "Done" sound finishes => optional logic
    public class DoneCompletionListener
        : Java.Lang.Object, Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly Action _onCompletion;
        public DoneCompletionListener(Action onCompletion)
        {
            _onCompletion = onCompletion;
        }
        public void OnCompletion(Android.Media.MediaPlayer mp)
        {
            _onCompletion?.Invoke();
        }
    }
#endif

    /// <summary>
    /// Simple data class for storing preset info.
    /// </summary>
    public class TimerPreset
    {
        public string Name { get; set; } = string.Empty;
        public int WorkoutTime { get; set; }
        public int RestTime { get; set; }
        public int Rounds { get; set; }

        // NEW: store which audio "key" was chosen
        public string WorkoutAudioKey { get; set; } = "Default";
        public string RestAudioKey { get; set; } = "Default";
    }


    public partial class MainPage : ContentPage
    {
        private System.Timers.Timer _timer;
        private int _workoutTime;
        private int _restTime;
        private int _currentTime;
        private bool _isWorkout = true;
        private int _rounds;
        private int _currentRound;

        // Collection of user-defined presets
        private List<TimerPreset> _presets = new();

#if ANDROID
        // MediaPlayers for workout, rest, alarm, and done
        private Android.Media.MediaPlayer? _workoutSound;
        private Android.Media.MediaPlayer? _restSound;
        private Android.Media.MediaPlayer? _alarmSound;
        private Android.Media.MediaPlayer? _doneSound;

        // Track current audio state for pause/resume
        private string? _currentMediaState = null;

        // Selected resource IDs for workout/rest
        private int _chosenWorkoutResId = Resource.Raw.workout;
        private int _chosenRestResId = Resource.Raw.rest;

        // Example dictionaries: "friendly name" -> resource ID
        private Dictionary<string, int> _workoutAudioMap = new()
        {
            { "Default", Resource.Raw.workout },
            // Add more as needed, e.g.:
            { "option 1", Resource.Raw.workout1 },
            { "option 2", Resource.Raw.workout2 },
            { "option 3", Resource.Raw.workout3 },
            { "option 4", Resource.Raw.workout4 },
        };

        private Dictionary<string, int> _restAudioMap = new()
{
    { "Default", Resource.Raw.rest },
    { "option 1", Resource.Raw.rest1 },
    { "option 2", Resource.Raw.rest2 },
    { "option 3", Resource.Raw.rest3 },
    { "option 4", Resource.Raw.rest4 },
};

#endif

        public MainPage()
        {
            InitializeComponent();

#if ANDROID
            // 1) Populate Pickers with dictionary keys
            foreach (var kvp in _workoutAudioMap)
            {
                WorkoutAudioPicker.Items.Add(kvp.Key);
            }
            foreach (var kvp in _restAudioMap)
            {
                RestAudioPicker.Items.Add(kvp.Key);
            }

            // Set default selections
            WorkoutAudioPicker.SelectedItem = "Default Workout";
            RestAudioPicker.SelectedItem = "Default Rest";

            // 2) Create alarm & done sounds
            _alarmSound = Android.Media.MediaPlayer.Create(
                Android.App.Application.Context,
                Resource.Raw.alarm);

            _doneSound = Android.Media.MediaPlayer.Create(
                Android.App.Application.Context,
                Resource.Raw.done);

            // 3) OnCompletion => next interval
            _alarmSound?.SetOnCompletionListener(new AlarmCompletionListener(() =>
            {
                MoveToNextInterval();
            }));

            // done sound OnCompletion => optional
            _doneSound?.SetOnCompletionListener(new DoneCompletionListener(() =>
            {
                // e.g., you could show a toast or alert
            }));
#endif

            // Setup a 1-second interval timer
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTimerElapsed;

            // Load previously saved presets
            LoadPresetsFromPreferences();

            // Initialize round label
            CurrentRoundLabel.Text = "Round 0 / 0";
        }

#if ANDROID
        /// <summary>
        /// Called when user picks a new workout audio in the picker.
        /// </summary>
        private void OnWorkoutAudioChanged(object sender, EventArgs e)
        {
            if (WorkoutAudioPicker.SelectedItem is string selectedKey)
            {
                if (_workoutAudioMap.TryGetValue(selectedKey, out int resId))
                {
                    _chosenWorkoutResId = resId;
                }
            }
        }

        /// <summary>
        /// Called when user picks a new rest audio in the picker.
        /// </summary>
        private void OnRestAudioChanged(object sender, EventArgs e)
        {
            if (RestAudioPicker.SelectedItem is string selectedKey)
            {
                if (_restAudioMap.TryGetValue(selectedKey, out int resId))
                {
                    _chosenRestResId = resId;
                }
            }
        }

        /// <summary>
        /// Create or recreate the workout MediaPlayer using the chosen resource ID.
        /// </summary>
        private void CreateWorkoutPlayer()
        {
            _workoutSound?.Release();
            _workoutSound = null;

            _workoutSound = Android.Media.MediaPlayer.Create(
                Android.App.Application.Context, _chosenWorkoutResId);
        }

        /// <summary>
        /// Create or recreate the rest MediaPlayer using the chosen resource ID.
        /// </summary>
        private void CreateRestPlayer()
        {
            _restSound?.Release();
            _restSound = null;

            _restSound = Android.Media.MediaPlayer.Create(
                Android.App.Application.Context, _chosenRestResId);
        }
#endif

        // ------------------------------------------------
        // 1. Preset Save/Load
        // ------------------------------------------------
        private void OnSavePresetClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PresetNameEntry.Text) ||
                !int.TryParse(WorkoutTimeEntry.Text, out var workoutTime) ||
                !int.TryParse(RestTimeEntry.Text, out var restTime) ||
                !int.TryParse(RoundsEntry.Text, out var rounds))
            {
                DisplayAlert("Error",
                    "Please enter a valid name, workout time, rest time, and rounds.",
                    "OK");
                return;
            }

            // Get the currently selected keys from the pickers
            string selectedWorkoutKey = (WorkoutAudioPicker.SelectedItem as string) ?? "Default";
            string selectedRestKey = (RestAudioPicker.SelectedItem as string) ?? "Default";

            var preset = new TimerPreset
            {
                Name = PresetNameEntry.Text,
                WorkoutTime = workoutTime,
                RestTime = restTime,
                Rounds = rounds,

                // NEW: store chosen keys
                WorkoutAudioKey = selectedWorkoutKey,
                RestAudioKey = selectedRestKey
            };

            _presets.Add(preset);

            PresetListView.ItemsSource = null;
            PresetListView.ItemsSource = _presets;

            SavePresetsToPreferences();
            DisplayAlert("Success", "Preset saved successfully.", "OK");
        }


        private void SavePresetsToPreferences()
        {
            var presetsJson = System.Text.Json.JsonSerializer.Serialize(_presets);
            Preferences.Set("SavedPresets", presetsJson);
        }

        private void LoadPresetsFromPreferences()
        {
            var presetsJson = Preferences.Get("SavedPresets", "[]");
            _presets = System.Text.Json.JsonSerializer
                .Deserialize<List<TimerPreset>>(presetsJson) ?? new List<TimerPreset>();

            PresetListView.ItemsSource = _presets;
        }

        private void OnUsePresetClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is TimerPreset preset)
            {
                WorkoutTimeEntry.Text = preset.WorkoutTime.ToString();
                RestTimeEntry.Text = preset.RestTime.ToString();
                RoundsEntry.Text = preset.Rounds.ToString();

                // NEW: Set the pickers to the stored keys
                WorkoutAudioPicker.SelectedItem = preset.WorkoutAudioKey;
                RestAudioPicker.SelectedItem = preset.RestAudioKey;

                DisplayAlert("Loaded",
                    $"Loaded preset: {preset.Name}",
                    "OK");
            }
        }


        private void OnDeletePresetClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is TimerPreset preset)
            {
                bool removed = _presets.Remove(preset);
                if (removed)
                {
                    PresetListView.ItemsSource = null;
                    PresetListView.ItemsSource = _presets;
                    SavePresetsToPreferences();
                }
            }
        }

        // ------------------------------------------------
        // 2. Timer Control Buttons
        // ------------------------------------------------
        private void OnStartButtonClicked(object sender, EventArgs e)
        {
            if (!int.TryParse(WorkoutTimeEntry.Text, out _workoutTime) ||
                !int.TryParse(RestTimeEntry.Text, out _restTime) ||
                !int.TryParse(RoundsEntry.Text, out _rounds))
            {
                DisplayAlert("Error",
                    "Please enter valid numbers for workout time, rest time, and rounds.",
                    "OK");
                return;
            }

            _currentRound = 1;
            _isWorkout = true;
            _currentTime = _workoutTime;
            CountdownLabel.Text = _currentTime.ToString();
            CurrentRoundLabel.Text = $"Round {_currentRound} / {_rounds}";

#if ANDROID
            // Recreate the players for the chosen workout/rest tracks
            CreateWorkoutPlayer();
            CreateRestPlayer();

            // Stop anything that might be playing
            PauseAllSounds(resetToZero: true);

            // Start the workout track
            StartPlayer(_workoutSound, fromPause: false);
            _currentMediaState = "workout";
#endif

            _timer.Start();
        }

        private void OnPauseButtonClicked(object sender, EventArgs e)
        {
            _timer.Stop();
#if ANDROID
            // Pause all current audio in place
            PausePlayer(_workoutSound, resetToZero: false);
            PausePlayer(_restSound, resetToZero: false);
            PausePlayer(_alarmSound, resetToZero: false);
            PausePlayer(_doneSound, resetToZero: false);
#endif
        }

        private void OnResumeButtonClicked(object sender, EventArgs e)
        {
            _timer.Start();

#if ANDROID
            // Resume whichever was playing
            if (_currentMediaState == "alarm")
            {
                StartPlayer(_alarmSound, fromPause: true);
            }
            else if (_currentMediaState == "workout")
            {
                StartPlayer(_workoutSound, fromPause: true);
            }
            else if (_currentMediaState == "rest")
            {
                StartPlayer(_restSound, fromPause: true);
            }
            else if (_currentMediaState == "done")
            {
                StartPlayer(_doneSound, fromPause: true);
            }
#endif
        }

        private void OnResetButtonClicked(object sender, EventArgs e)
        {
            _timer.Stop();
            _currentRound = 1;
            _isWorkout = true;
            _currentTime = 0;
            CountdownLabel.Text = "0";
            CurrentRoundLabel.Text = $"Round 0 / 0";

#if ANDROID
            PauseAllSounds(resetToZero: true);
            _currentMediaState = null;
#endif
        }

        // ------------------------------------------------
        // 3. Timer Tick Logic
        // ------------------------------------------------
        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _currentTime--;

                if (_currentTime <= 0)
                {
                    _timer.Stop();
#if ANDROID
                    PauseAllSounds(resetToZero: false);
                    // Switch to alarm sound
                    StartPlayer(_alarmSound, fromPause: false);
                    _currentMediaState = "alarm";
#endif
                    return;
                }

                CountdownLabel.Text = _currentTime.ToString();
            });
        }

        // ------------------------------------------------
        // 4. Move to Next Interval AFTER Alarm
        // ------------------------------------------------
        private void MoveToNextInterval()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isWorkout)
                {
                    // Just finished workout => now rest
                    _currentTime = _restTime;
                    _isWorkout = false;
                }
                else
                {
                    // Just finished rest => increment round
                    _currentRound++;
                    if (_currentRound > _rounds)
                    {
                        // Done!
                        CountdownLabel.Text = "Done!";
                        CurrentRoundLabel.Text = $"Round {_rounds} / {_rounds}";

#if ANDROID
                        PauseAllSounds(resetToZero: true);
                        StartPlayer(_doneSound, fromPause: false);
                        _currentMediaState = "done";
#endif
                        return;
                    }
                    _currentTime = _workoutTime;
                    _isWorkout = true;
                }

                // Update UI
                CurrentRoundLabel.Text = $"Round {_currentRound} / {_rounds}";
                CountdownLabel.Text = _currentTime.ToString();

#if ANDROID
                // Start the next interval's sound
                PauseAllSounds(resetToZero: true);
                if (_isWorkout)
                {
                    StartPlayer(_workoutSound, fromPause: false);
                    _currentMediaState = "workout";
                }
                else
                {
                    StartPlayer(_restSound, fromPause: false);
                    _currentMediaState = "rest";
                }
#endif
                _timer.Start();
            });
        }

#if ANDROID
        // ------------------------------------------------
        // 5. MediaPlayer Helpers
        // ------------------------------------------------
        private void StartPlayer(Android.Media.MediaPlayer? player, bool fromPause)
        {
            if (player == null) return;
            try
            {
                if (!player.IsPlaying)
                {
                    // If NOT resuming, reset position
                    if (!fromPause)
                    {
                        player.SeekTo(0);
                    }
                    player.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting player: {ex.Message}");
            }
        }

        private void PausePlayer(Android.Media.MediaPlayer? player, bool resetToZero)
        {
            if (player == null) return;

            if (player.IsPlaying)
            {
                player.Pause();
            }
            if (resetToZero)
            {
                player.SeekTo(0);
            }
        }

        private void PauseAllSounds(bool resetToZero)
        {
            PausePlayer(_workoutSound, resetToZero);
            PausePlayer(_restSound, resetToZero);
            PausePlayer(_alarmSound, resetToZero);
            PausePlayer(_doneSound, resetToZero);
        }
#endif

        // ------------------------------------------------
        // 6. Cleanup
        // ------------------------------------------------
        protected override void OnDisappearing()
        {
            base.OnDisappearing();

#if ANDROID
            _workoutSound?.Release();
            _workoutSound = null;

            _restSound?.Release();
            _restSound = null;

            _alarmSound?.Release();
            _alarmSound = null;

            _doneSound?.Release();
            _doneSound = null;
#endif
        }
    }
}

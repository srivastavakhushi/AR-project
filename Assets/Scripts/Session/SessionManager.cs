using System;
using UnityEngine;
using UnityEngine.InputSystem;
using CrimeSceneVR.Session;

namespace CrimeSceneVR.Session
{
    /// <summary>
    /// Central manager for research participant sessions:
    /// Manages session lifecycle, participant ID, experimental condition, and start/end timing.
    /// Integrates with CrimeSceneEvidenceManager and triggers telemetry and CSV export pipelines.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    [AddComponentMenu("Crime Scene VR/Session/Session Manager")]
    public class SessionManager : MonoBehaviour
    {
        private static SessionManager _instance;
        public static SessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SessionManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Default Session Configuration")]
        [Tooltip("Participant identifier assigned if not specified at runtime.")]
        [SerializeField] private string _defaultParticipantId = "P001";

        [Tooltip("Experimental condition (e.g., VR_Standard, VR_VisualCue, Desktop_Baseline).")]
        [SerializeField] private string _defaultCondition = "VR_Standard";

        [Tooltip("If true, automatically starts a default session on Start().")]
        [SerializeField] private bool _autoStartSession = true;

        [Header("Editor Keyboard Controls")]
        [Tooltip("Allow starting/ending sessions via F1/F2 keys in Play Mode when keyboard exists.")]
        [SerializeField] private bool _enableHotkeys = true;

        [Header("Active Session Status")]
        [SerializeField] private SessionData _currentSession;

        private float _sessionStartTime = 0f;
        private CrimeSceneEvidenceManager _evidenceManager;
        private bool _endingSession;

        public SessionData CurrentSession => _currentSession;
        public bool IsSessionActive => _currentSession != null && _currentSession.Status == SessionStatus.InProgress;
        public float ElapsedSessionTime => IsSessionActive ? (Time.unscaledTime - _sessionStartTime) : (_currentSession != null ? _currentSession.DurationSeconds : 0f);

        public event Action<SessionData> OnSessionStarted;
        public event Action<SessionData> OnSessionEnded;

        private void Awake()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
            }
            else
            {
                Debug.LogWarning("[SessionManager] Duplicate instance detected. Destroying duplicate.", this);
                Destroy(this);
                return;
            }
        }

        private void Start()
        {
            HookEvidenceManager();

            if (Application.isPlaying && _autoStartSession && !IsSessionActive)
            {
                StartSession(_defaultParticipantId, _defaultCondition);
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // VR Controller X or Y button to finish & submit session from inside headset
            // Only allow manual completion if at least 1 evidence has been tagged (prevents accidental session end on keyboard WASD/X)
            try
            {
                if (IsSessionActive && (_evidenceManager == null || _evidenceManager.TaggedCount > 0) &&
                    (OVRInput.GetDown(OVRInput.RawButton.X) || OVRInput.GetDown(OVRInput.RawButton.Y)))
                {
                    EndSession();
                    return;
                }
            }
            catch
            {
                // Ignore when OVR runtime is inactive
            }

            if (!_enableHotkeys) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // 'R' or 'F1' key restarts a fresh session at any time
                if (keyboard.rKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame)
                {
                    StartSession(_defaultParticipantId, _defaultCondition);
                }
                else if (keyboard.f2Key.wasPressedThisFrame)
                {
                    if (IsSessionActive)
                    {
                        EndSession();
                    }
                    else
                    {
                        Debug.LogWarning("[SessionManager] Cannot end session: No active session is running.");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            UnhookEvidenceManager();
            if (_instance == this)
                _instance = null;
        }

        private void HookEvidenceManager()
        {
            if (_evidenceManager == null)
            {
                _evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
            }

            if (_evidenceManager != null)
            {
                _evidenceManager.SessionCompleted -= HandleEvidenceSessionCompleted;
                _evidenceManager.SessionCompleted += HandleEvidenceSessionCompleted;
            }
        }

        private void UnhookEvidenceManager()
        {
            if (_evidenceManager != null)
            {
                _evidenceManager.SessionCompleted -= HandleEvidenceSessionCompleted;
            }
        }

        private void HandleEvidenceSessionCompleted()
        {
            if (IsSessionActive && !_endingSession)
            {
                Debug.Log("[SessionManager] Evidence manager reported session completion. Finalizing research session.");
                EndSession();
            }
        }

        /// <summary>
        /// Starts a new research participant session.
        /// </summary>
        public SessionData StartSession(string participantId = null, string condition = null)
        {
            if (IsSessionActive)
            {
                Debug.LogWarning($"[SessionManager] Ending ongoing session '{_currentSession.SessionId}' before starting a new one.");
                EndSession();
            }

            string pId = string.IsNullOrWhiteSpace(participantId) ? _defaultParticipantId : participantId;
            string cond = string.IsNullOrWhiteSpace(condition) ? _defaultCondition : condition;

            _currentSession = new SessionData(pId, cond);
            _sessionStartTime = Time.unscaledTime;

            if (_evidenceManager != null && (!_evidenceManager.IsSessionStarted || _evidenceManager.IsSessionCompleted))
            {
                _evidenceManager.StartSession();
            }

            Debug.Log($"[SessionManager] Research session started: ID={_currentSession.SessionId}, Participant={_currentSession.ParticipantId}, Condition={_currentSession.Condition}");

            OnSessionStarted?.Invoke(_currentSession);
            return _currentSession;
        }

        /// <summary>
        /// Ends the current active research session, syncs CrimeSceneEvidenceManager, and calculates duration.
        /// </summary>
        public SessionData EndSession()
        {
            if (!IsSessionActive)
            {
                Debug.LogWarning("[SessionManager] No active session to end.");
                return _currentSession;
            }

            if (_endingSession)
                return _currentSession;

            _endingSession = true;
            try
            {
                float duration = ElapsedSessionTime;
                _currentSession.MarkComplete(duration);

                if (_evidenceManager != null && _evidenceManager.IsSessionStarted && !_evidenceManager.IsSessionCompleted)
                {
                    _evidenceManager.CompleteSession();
                }

                Debug.Log($"[SessionManager] Research session ended: ID={_currentSession.SessionId}, Duration={duration:F2}s");
                OnSessionEnded?.Invoke(_currentSession);
                return _currentSession;
            }
            finally
            {
                _endingSession = false;
            }
        }
    }
}

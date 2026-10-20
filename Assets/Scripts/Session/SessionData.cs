using System;
using UnityEngine;

namespace CrimeSceneVR.Session
{
    public enum SessionStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Aborted
    }

    /// <summary>
    /// Encapsulates metadata, participant identity, experimental condition, and lifecycle state.
    /// </summary>
    [Serializable]
    public class SessionData
    {
        [SerializeField] private string _participantId;
        [SerializeField] private string _condition;
        [SerializeField] private string _sessionId;
        [SerializeField] private SessionStatus _status = SessionStatus.NotStarted;
        [SerializeField] private string _startTimeUtc;
        [SerializeField] private string _endTimeUtc;
        [SerializeField] private float _durationSeconds;

        public string ParticipantId => _participantId;
        public string Condition => _condition;
        public string SessionId => _sessionId;
        public SessionStatus Status => _status;
        public string StartTimeUtc => _startTimeUtc;
        public string EndTimeUtc => _endTimeUtc;
        public float DurationSeconds => _durationSeconds;

        public SessionData(string participantId, string condition, string sessionId = null)
        {
            _participantId = string.IsNullOrWhiteSpace(participantId) ? "P001" : participantId.Trim();
            _condition = string.IsNullOrWhiteSpace(condition) ? "Default" : condition.Trim();
            
            DateTime now = DateTime.UtcNow;
            _sessionId = string.IsNullOrWhiteSpace(sessionId) 
                ? $"{_participantId}_{_condition}_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}"
                : sessionId.Trim();

            _status = SessionStatus.InProgress;
            _startTimeUtc = now.ToString("o"); // ISO 8601
            _endTimeUtc = string.Empty;
            _durationSeconds = 0f;
        }

        public void MarkComplete(float elapsedSeconds)
        {
            _status = SessionStatus.Completed;
            _endTimeUtc = DateTime.UtcNow.ToString("o");
            _durationSeconds = Mathf.Max(0f, elapsedSeconds);
        }

        public void MarkAborted(float elapsedSeconds)
        {
            _status = SessionStatus.Aborted;
            _endTimeUtc = DateTime.UtcNow.ToString("o");
            _durationSeconds = Mathf.Max(0f, elapsedSeconds);
        }
    }
}

namespace Lumen.Domain.Sessions;

public enum TutorState
{
    Idle,
    LoadingLesson,
    Teaching,
    Listening,
    Answering,
    CheckingUnderstanding,
    Adapting,
    Paused,
    LessonComplete
}

using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// The student's register pulls the tutor's, never the other way round. A tutor that opens in
/// Pidgin at a student who did not invite it has made an assumption about them from nothing.
/// </summary>
public class RegisterLadderTests
{
    [Fact]
    public void One_stray_code_switch_is_not_an_invitation()
    {
        Assert.Equal(
            RegisterLevel.StandardEnglish,
            RegisterLadder.Observe(RegisterLevel.StandardEnglish, consecutiveStudentCodeSwitches: 1));
    }

    [Fact]
    public void Repeated_code_switching_opens_the_register_one_step()
    {
        Assert.Equal(
            RegisterLevel.LightInterjection,
            RegisterLadder.Observe(RegisterLevel.StandardEnglish, RegisterLadder.EvidenceToRise));
    }

    [Fact]
    public void It_climbs_one_step_at_a_time_rather_than_jumping_to_the_top()
    {
        Assert.Equal(
            RegisterLevel.ComfortableCodeSwitch,
            RegisterLadder.Observe(RegisterLevel.LightInterjection, consecutiveStudentCodeSwitches: 9));
    }

    [Fact]
    public void There_is_a_ceiling()
    {
        Assert.Equal(
            RegisterLevel.ComfortableCodeSwitch,
            RegisterLadder.Observe(RegisterLevel.ComfortableCodeSwitch, consecutiveStudentCodeSwitches: 99));
    }

    [Fact]
    public void Asking_for_plain_english_is_honoured_at_once_and_in_full()
    {
        // Rising takes evidence; falling takes one signal. The asymmetry is the point.
        Assert.Equal(RegisterLevel.StandardEnglish, RegisterLadder.RequestedPlain());
    }
}

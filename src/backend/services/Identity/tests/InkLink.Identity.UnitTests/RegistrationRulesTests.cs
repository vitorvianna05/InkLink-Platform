using InkLink.Identity.Application.Registration;
using InkLink.Identity.Domain;

namespace InkLink.Identity.UnitTests;

/// <summary>Regras do cadastro e dos códigos de confirmação (decisão 0020).</summary>
public sealed class RegistrationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pending_profile_becomes_active_only_after_terms_email_and_phone_in_any_order()
    {
        var profile = Profile.CreatePending("Nova Pessoa");
        Assert.True(profile.RegistrationPending);
        profile.ChangePhone("+5511987654321");
        profile.ConfirmPhone("+5511987654321", Now);
        profile.ConfirmEmail("nova@example.test", Now);
        Assert.True(profile.RegistrationPending);
        profile.AcceptTerms("v1", Now);
        Assert.False(profile.RegistrationPending);
        Assert.False(Profile.Create("Administrativa").RegistrationPending);
    }

    [Fact]
    public void Changing_the_phone_requires_a_new_confirmation_and_artist_request_needs_an_active_account()
    {
        var profile = Profile.CreatePending("Nova Pessoa");
        Assert.Throws<InvalidOperationException>(() => profile.RequestArtist(Now));
        profile.ChangePhone("+5511987654321");
        profile.ConfirmPhone("+5511987654321", Now);
        profile.ChangePhone("+5511912345678");
        Assert.Null(profile.PhoneConfirmedAt);
        Assert.Throws<InvalidOperationException>(() => profile.ConfirmPhone("+5511987654321", Now));

        var active = Profile.Create("Cliente");
        Assert.Equal(ArtistRequest.None, active.ArtistRequestStatus);
        Assert.True(active.RequestArtist(Now));
        Assert.False(active.RequestArtist(Now));
        Assert.Equal(ArtistRequest.Pending, active.ArtistRequestStatus);
        active.ActivateArtist();
        Assert.Equal(ArtistRequest.Approved, active.ArtistRequestStatus);
    }

    [Fact]
    public void Code_is_single_use_expires_and_closes_after_five_wrong_attempts()
    {
        var code = VerificationCode.Issue(Guid.NewGuid(), ContactChannel.Email, "a@example.test", "abc", Now);
        Assert.Equal(CodeCheck.Confirmed, code.Check("abc", Now.AddMinutes(9)));
        Assert.Equal(CodeCheck.Expired, code.Check("abc", Now.AddMinutes(9)));

        var late = VerificationCode.Issue(Guid.NewGuid(), ContactChannel.Email, "a@example.test", "abc", Now);
        Assert.Equal(CodeCheck.Expired, late.Check("abc", Now.Add(VerificationCode.Validity)));

        var guessed = VerificationCode.Issue(Guid.NewGuid(), ContactChannel.Phone, "+5511987654321", "abc", Now);
        for (var attempt = 1; attempt < VerificationCode.MaxAttempts; attempt++) Assert.Equal(CodeCheck.Invalid, guessed.Check("xyz", Now));
        Assert.Equal(CodeCheck.Exhausted, guessed.Check("xyz", Now));
        Assert.Equal(CodeCheck.Expired, guessed.Check("abc", Now));
    }

    [Theory]
    [InlineData("(11) 98765-4321", "+5511987654321")]
    [InlineData("1133334444", "+551133334444")]
    [InlineData("+55 21 91234-5678", "+5521912345678")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    [InlineData("12345", null)]
    [InlineData("011987654321", null)]
    [InlineData("11 9876a4321", null)]
    [InlineData("", null)]
    public void Phone_numbers_are_normalized_to_e164(string input, string? expected) =>
        Assert.Equal(expected, ManageRegistration.NormalizePhone(input));
}

using OnderCode.Application.Results;
using OnderCode.Kernel.Validation;

namespace OnderCode.Application.Tests;

public sealed class TurkishIdentityTests
{
    [Theory]
    [InlineData("10000000146", true)]
    [InlineData("12345678950", true)]
    [InlineData(" 11111111110 ", true)]
    [InlineData("10000000147", false)]
    [InlineData("02345678950", false)]
    [InlineData("1234567895", false)]
    [InlineData("1234567895a", false)]
    [InlineData(null, false)]
    public void Tckn(string? value, bool expected) => Assert.Equal(expected, TurkishIdentity.IsValidTckn(value));

    [Theory]
    [InlineData("1111111114", true)]
    [InlineData("0123456789", true)]
    [InlineData("1111111111", false)]
    [InlineData("123456789", false)]
    [InlineData(null, false)]
    public void Vkn(string? value, bool expected) => Assert.Equal(expected, TurkishIdentity.IsValidVkn(value));

    [Theory]
    [InlineData("TR33 0006 1005 1978 6457 8413 26", true)]
    [InlineData("tr330006100519786457841326", true)]
    [InlineData("TR330006100519786457841327", false)]
    [InlineData("TR3300061005197864578413", false)]
    [InlineData("", false)]
    public void Iban(string? value, bool expected) => Assert.Equal(expected, TurkishIdentity.IsValidIban(value));
}

public sealed class ResultTests
{
    [Fact]
    public void Failure_RequiresAtLeastOneError()
        => Assert.Throws<ArgumentException>(() => Result.Failure(new List<Error>()));

    [Fact]
    public void ImplicitConversions_MakeHandlersReadable()
    {
        Result<int> ok = 42;
        Result<int> failed = Error.NotFound("Yok");

        Assert.Equal(42, ok.Value);
        Assert.True(failed.IsFailure);
        Assert.Equal("Yok", failed.ErrorMessage);
    }
}

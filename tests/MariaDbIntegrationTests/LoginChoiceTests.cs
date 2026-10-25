using HeimevernetInnlevering1.Models;
using Xunit;

namespace MariaDbIntegrationTests;

public class LoginChoiceTests
{
    [Theory]
    [InlineData("Bruker", false, true)]
    [InlineData("Bruker", true, true)]
    [InlineData("Admin", false, false)]
    [InlineData("Admin", true, true)]
    [InlineData("Administrator", true, false)]
    [InlineData("invalid", true, false)]
    [InlineData("", false, false)]
    public void ChoiceNeverGrantsAdmin(string choice, bool storedAdminRole, bool expected)
    {
        Assert.Equal(expected, AccountLoginRules.CanUseChoice(choice, storedAdminRole));
    }
}

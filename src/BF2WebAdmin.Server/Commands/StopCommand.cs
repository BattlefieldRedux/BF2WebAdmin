using BF2WebAdmin.Common;
using BF2WebAdmin.Server.Abstractions;
using BF2WebAdmin.Server.Extensions;

namespace BF2WebAdmin.Server.Commands
{
    [Command("stop", Auth.Admin)]
    public class StopCommand : BaseCommand
    {
    }
}
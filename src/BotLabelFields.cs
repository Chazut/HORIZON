using System;

namespace Horizon;

[Flags]
internal enum BotLabelFields
{
    None = 0,
    Name = 1 << 0,
    Type = 1 << 1,
    RoleDetail = 1 << 2,
    Personality = 1 << 3,
    Distance = 1 << 4,
    Health = 1 << 5,
    CarriedValue = 1 << 6,
    Ghost = 1 << 7,
    Action = 1 << 8,
    SquadTactic = 1 << 9,

    Actions = Action | SquadTactic,
    Behavior = Personality | Ghost | Actions,
    HostData = Health | CarriedValue | Behavior,
    Human = Name | Type | Distance | Health,
    All = Human | RoleDetail | CarriedValue | Behavior
}

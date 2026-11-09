// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Archives;

public readonly record struct GameRoleIdentityId
{
    public static readonly Guid DeterministicNamespace =
        Guid.Parse("4252df90-5dca-4b4f-a01a-8c8026d19bc2");

    public GameRoleIdentityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Game role identity ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static GameRoleIdentityId FromNaturalIdentity(
        GameRoleNaturalIdentity naturalIdentity)
    {
        ArgumentNullException.ThrowIfNull(naturalIdentity);

        return new GameRoleIdentityId(
            UuidVersion5.Create(
                DeterministicNamespace,
                naturalIdentity.ToDeterministicName()));
    }

    public override string ToString() => Value.ToString("D");
}

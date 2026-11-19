// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Windows.Input;

namespace FurinaChronicle.Presentation.Gacha;

// These presentation actions are guarded by their models and the host's operation lock.
public sealed class GachaActionCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

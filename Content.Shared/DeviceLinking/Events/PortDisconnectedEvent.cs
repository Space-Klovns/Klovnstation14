// SPDX-FileCopyrightText: 2021 Paul Ritter
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 AJCM-git
// SPDX-FileCopyrightText: 2023 Julian Giebel
// SPDX-FileCopyrightText: 2026 Gerkada
//
// SPDX-License-Identifier: MIT

namespace Content.Shared.DeviceLinking.Events
{
    public sealed class PortDisconnectedEvent : EntityEventArgs
    {
        public readonly string Port;

        public readonly EntityUid Source;

        public readonly EntityUid Sink;

        public PortDisconnectedEvent(string port, EntityUid source, EntityUid sink)
        {
            Port = port;
            Source = source;
            Sink = sink;
        }
    }
}

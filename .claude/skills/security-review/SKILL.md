---
name: security-review
description: Office role skill for the Tech Leader. Security checks for the Automation tool, especially licensing and secrets.
user-invocable: false
---

# Security review (Automation)

- Secrets: the private key lives in `keys/` (never committed) and an embedded secret in
  `src/AutomationTest.Core/Licensing/Secrets.cs`. Never print secret values in reports or docs; refer to them by location.
- Licensing runs client-side in a .NET assembly that can be decompiled. Rate risks realistically and propose
  proportionate mitigations (key rotation, signature-only verification with a public key, obfuscation, code signing).
- Trusted time (`TrustedClock.cs`): check fallbacks, timeouts and what happens offline or with a changed system clock.
- Input handling: script JSON and `Setting.ini` are user files; malformed input must fail safely with a clear message.
- Process launch (Utilities/Open): check quoting of paths and arguments.
- Report findings as must fix / should fix / nice to have with `file:line`.

# Contributing

Thank you for helping improve WireGuard Server Manager.

1. Open an issue describing the bug or proposed change.
2. Fork the repository and create a focused branch.
3. Keep server operations constrained and never add arbitrary shell execution.
4. Add or update automated tests for behavior changes.
5. Run `dotnet build` and `dotnet test` in Release configuration.
6. Submit a pull request with a concise explanation and verification steps.

Never commit real server addresses, passwords, SSH keys, WireGuard private
keys, client configuration files, QR codes, or runtime databases.

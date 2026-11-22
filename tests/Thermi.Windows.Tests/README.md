# Thermi.Windows.Tests

Tests that need Windows: real named pipes, the service wrapper, the painter, the settings file and the window class. They use a fake clock wherever time matters, and never need admin rights.

`scripts/check.ps1` runs them in their own step, outside the coverage of `src/Thermi.Core`.

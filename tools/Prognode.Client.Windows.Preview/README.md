# PROGNODE Client Windows — Connectivity Preview

Purpose: validate the existing Core Settings workflow before the final Windows Client UI is built.

This preview does four things only:
1. broadcasts `PROGNODE_DISCOVER_V1` on UDP 5081;
2. lists discovered PROGNODE Servers without asking for an IP;
3. pairs with the 6-digit code shown in Core > Settings;
4. calls `/api/overview` with the returned Bearer token.

The pairing token is intentionally kept in memory only in this preview. Secure persisted credential storage and the final dashboard shell belong to the production Client milestone.

Build on Windows:

```powershell
dotnet build .\Prognode.Client.Windows.Preview.csproj -c Release
dotnet run --project .\Prognode.Client.Windows.Preview.csproj
```

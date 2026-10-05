# 本機資料庫連線修復 — 2026-10-05

Owner 授權找出復發根因並修復本機資料庫連線。本次是環境維修，沒有修改業務程式、Schema、API 契約、timeout、驗收計畫或 Round 狀態。Git 起始工作目錄乾淨。

## 根因與證據

- PostgreSQL 原容器 `8f9321fa7bf7` 內部 healthy，Compose 已宣告 `127.0.0.1:5432:5432`；HostConfig.PortBindings 正確，NetworkSettings.Ports 實際為 `{"5432/tcp":[]}`，主機沒有 5432 listener。
- IPv4／IPv6 TCP 動態範圍都為 start `1024`、count `13977`，即 `1024–15000`；active exclusion `5417–5516` 包含 5432。主機 TcpListener bind 實測回 `10013 AccessDenied`。
- 2026-10-04 的修復只重啟 WinNAT，沒有改變動態範圍，因此只釋放當次保留。本次重啟後相同低位動態範圍又與資料庫 port 衝突。這是對復發機制的診斷；尚未辨識最初修改動態範圍的軟體或操作。
- [Microsoft 文件](https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/tcp-ip-port-exhaustion-troubleshooting) 記載 Windows 預設動態範圍為 `49152–65535`。將低位服務 port 放入動態範圍，使其受到主機動態保留影響；Compose 宣告本身不缺 ports。

## 已執行修復

2026-10-05 臺北時間約 18:09–18:12：先保存七張 Domain Table（users 排除 password_hash）及 migration 的一致唯讀快照，不輸出 credential。管理員 helper 執行以下兩個命令，均成功，再 Stop／Start WinNAT，finally 確認 Running：

```powershell
netsh interface ipv4 set dynamicportrange protocol=tcp startport=49152 numberofports=16384
netsh interface ipv6 set dynamicportrange protocol=tcp startport=49152 numberofports=16384
```

修復後 active exclusion 只剩 `50000–50059`／`52353`，不再包含 5432，主機 TcpListener bind 成功。`docker compose config --quiet` 成功後執行：

```powershell
docker compose up -d --no-deps --pull never --force-recreate postgres
docker compose restart postgres
```

兩個 Compose 操作均 exit 0，只處理 postgres service，沿用 `custom-form-service-postgres-data`；未使用 `down -v`。原 API process 持續使用，未另 build 或重新啟動 API。沒有修改 `.env`、User Secrets、DB port 或 UDP 範圍。

## 實際驗證

| 檢查 | 實際結果 |
| --- | --- |
| 主機 TCP connect `127.0.0.1:5432` | PASS |
| 實際 Docker mapping | `127.0.0.1:5432 -> 5432/tcp` |
| 容器 health | healthy |
| `Invoke-WebRequest http://localhost:5000/health/ready` | `200 Healthy` |
| 正常容器 restart 後 readiness | 啟動瞬間首次 `503 Unhealthy`；待容器 healthy 後 `200 Healthy` |
| 七表與 migration 修復前／後／重啟後完整非秘密快照 | 全部完全相等 |
| 保留資料列 | 4 users、0 identities、14 forms、32 questions、15 options、9 responses、10 answers、1 migration |
| 最後 IPv4／IPv6 TCP 動態範圍 | `49152–65535` |

初次快照 shell 命令因 PowerShell 引號造成 ParserError，尚未執行操作；改以 here-string 後快照成功。Docker 在一般 sandbox 回 Access denied，改用獲核准的提權 shell 讀取／維修。以上失敗没有當作 PASS，也未更動既有驗收歷史。

本次沒有產品碼變更，未重跑 build、全套測試或正式驗收。沒有重開整台 Windows；重開機後的行為尚未驗證。若外部軟體再次把動態範圍改回低位，仍可能復發，屆時應先比對動態範圍。

非秘密網路結果、管理員 helper 結果、readiness 及修復前後快照保留於本機 ignored 目錄 [`artifacts/db-connectivity-repair-20261005/`](../../artifacts/db-connectivity-repair-20261005/)，未提交資料快照或秘密。操作指南已補入 [environment-configuration.md](../configuration/environment-configuration.md#windows-postgresql-host-port-troubleshooting)，README 新增入口。只提交這三份說明文件，commit SHA 與最後 Git 狀態見交付訊息。

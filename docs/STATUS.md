# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Giai đoạn: **đã chốt thiết kế, chưa có code**. Chi tiết xem `docs/PROJECT.md`.
- Repo chỉ có nhánh `main` (1 commit "Initial commit"). Chưa có nhánh `preview`.
- Các file `CLAUDE.md`, `.claude/` và `docs/` chưa được commit.

## Đã làm
- [x] Thống nhất yêu cầu và công nghệ (C# / .NET Framework 4.8 / WinForms, chỉ Windows).
- [x] Chốt các quyết định: tọa độ theo cửa sổ, license theo máy, chưa ký số.
- [x] Ghi tài liệu dự án (`docs/PROJECT.md`) và status (file này).

- [x] Nhánh `preview` đã tạo; PR #1 (docs + cấu hình Claude) đã merge vào `preview`.
- [x] Cài .NET SDK 10.0.403 (winget). Dùng để build project net48.
- [x] Milestone 1 (code): solution, `Automation.Core` (netstandard2.0, `MouseController` Move/Down/Up/Click), `Automation.App` (net48 WinForms smoke-test form, manifest DPI PerMonitorV2). `dotnet build -c Release` thành công, 0 warning. Nhánh `feature_m1_scaffold`.

## Việc tiếp theo
- [ ] Milestone 1: người dùng tự chạy `Automation.App.exe` thấy hoạt động; cần xác nhận Move/Click đúng, rồi PR vào `preview`.
- [ ] Các milestone 2–5 theo `docs/PROJECT.md`.

## Chặn
- Không còn bị chặn. Khi Claude chạy exe/dll từ công cụ của nó thì Windows báo "An Application Control policy has blocked this file", nhưng người dùng chạy exe trực tiếp thì chạy bình thường. Nguyên nhân chưa xác định (registry `VerifiedAndReputablePolicyState = 1` chỉ là gợi ý, chưa kết luận được). Đừng suy ra từ đây rằng máy khác sẽ bị chặn hay không; cần thử trên máy sạch trước khi phát hành.
- Vì Claude không chạy được exe, việc test lúc chạy phải do người dùng làm hoặc dùng unit test.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Khi hoàn thành mỗi milestone, cập nhật file này (mục Đã làm / Việc tiếp theo).

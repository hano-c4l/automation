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
- [ ] Milestone 1: **chưa chạy thử được** (xem "Chặn" bên dưới), rồi mới PR vào `preview`.
- [ ] Các milestone 2–5 theo `docs/PROJECT.md`.

## Chặn
- Máy dev bật **Smart App Control** (`VerifiedAndReputablePolicyState = 1`): Windows chặn nạp/chạy mọi exe/dll chưa ký tự build ("An Application Control policy has blocked this file"). Chưa test được lúc chạy trên máy này.
- Hướng xử lý cần người dùng quyết định (Claude không được đổi cài đặt bảo mật): tắt Smart App Control (một chiều, không bật lại được nếu không reset Windows), test trên máy/VM khác, hoặc ký số bằng chứng chỉ từ CA tin cậy. Điều này làm vấn đề ký số quan trọng hơn dự kiến: máy người dùng cuối bật Smart App Control cũng sẽ chặn exe chưa ký.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Khi hoàn thành mỗi milestone, cập nhật file này (mục Đã làm / Việc tiếp theo).

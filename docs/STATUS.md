# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Đang ở **milestone 2** (giao diện + lưu/mở + phím nóng): code xong, build sạch, chờ người dùng test giao diện.
- Nhánh: `preview` (docs + cấu hình Claude, PR #1), `feature_m1_scaffold` (M1, chưa push), `feature_m2_ui` (M2, cắt từ M1, chưa push). M1 và M2 chưa PR vào `preview`; khi người dùng nói "PR into preview" thì push và PR (M2 chứa luôn M1).

## Đã làm
- [x] Thống nhất yêu cầu và công nghệ (C# / .NET Framework 4.8 / WinForms, chỉ Windows). Quyết định chi tiết trong `docs/PROJECT.md`.
- [x] Nhánh `preview` đã tạo; PR #1 (docs + cấu hình Claude) đã merge vào `preview`.
- [x] Cài .NET SDK 10.0.403 (winget) để build project net48.
- [x] **M1:** solution, `Automation.Core` (netstandard2.0) với `MouseController` (Move có easing theo thời gian, Down, Up, Click), `Automation.App` (net48 WinForms, manifest DPI PerMonitorV2). Người dùng đã test: move, delay, click trái/phải đều đúng.
- [x] **M2 (code):**
  - Core: `Step`, `AutomationScript`, `ScriptSerializer` (JSON thụt lề bằng `DataContractJsonSerializer`, enum ghi dạng chữ, có `Version`), `ScriptRunner` (lặp vòng, lặp từng bước, delay, hủy bằng token, tự nhả nút chuột đang giữ khi dừng).
  - App: danh sách bước (Add/Update/Remove/Up/Down), overlay chọn điểm toàn màn hình (`PointPickerForm`), Open/Save/New, phím nóng toàn cục **F6** Start/Stop và **Esc** dừng khẩn cấp (Esc chỉ đăng ký khi đang chạy), cửa sổ tự thu nhỏ khi chạy.
  - Đã kiểm tra bằng code thật (qua PowerShell): lưu/mở JSON đúng giá trị (cả tọa độ âm), runner đúng thời gian và hủy được giữa chừng. **Chưa test được giao diện** (Claude không điều khiển được UI).

## Việc tiếp theo
- [ ] Người dùng test giao diện M2 (xem checklist bên dưới), báo lỗi nếu có.
- [ ] PR M2 vào `preview` khi người dùng yêu cầu.
- [ ] M3: tọa độ theo cửa sổ; M4: gửi phím/text, nhận diện ảnh; M5: license (xem `docs/PROJECT.md`).

### Checklist test M2
1. Add vài bước, chọn bước rồi Update/Remove/Up/Down có đúng không.
2. "Pick on screen..." phủ được mọi màn hình, click lấy đúng tọa độ, Esc hủy.
3. Start bằng nút và bằng F6; Esc hoặc F6 dừng được cả lúc chuột đang move chậm.
4. Save rồi New rồi Open lại, các bước và setting giống như cũ.
5. Loops = 0 chạy mãi đến khi dừng; MouseDown rồi dừng giữa chừng thì nút chuột được nhả.

## Ghi chú môi trường
- Khi Claude chạy exe từ công cụ của nó, Windows có lúc báo "An Application Control policy has blocked this file" (trước đó), nhưng người dùng chạy trực tiếp thì bình thường, và về sau PowerShell của Claude nạp được DLL. Nguyên nhân chưa xác định; cần thử trên máy sạch trước khi phát hành.
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write thay vì `cat <<EOF`.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- Khi hoàn thành mỗi milestone, cập nhật file này.

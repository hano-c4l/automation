# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Đang ở **milestone 3** (tọa độ theo cửa sổ): code xong, build sạch, đã kiểm tra Core bằng cửa sổ test thật; chờ người dùng test giao diện.
- M1 + M2 đã merge vào `preview` (PR #2). Nhánh đang làm: `feature_m3_window_coords` (cắt từ `preview`, chưa push). Khi người dùng nói "PR into preview" thì push và PR.

## Đã làm
- [x] Thống nhất yêu cầu và công nghệ (C# / .NET Framework 4.8 / WinForms, chỉ Windows). Quyết định chi tiết trong `docs/PROJECT.md`.
- [x] Nhánh `preview` đã tạo; PR #1 (docs + cấu hình Claude) và PR #2 (M1 + M2) đã merge vào `preview`.
- [x] Cài .NET SDK 10.0.403 (winget) để build project net48.
- [x] **M1:** solution, `Automation.Core` (netstandard2.0) với `MouseController` (Move có easing theo thời gian, Down, Up, Click), `Automation.App` (net48 WinForms, manifest DPI PerMonitorV2). Người dùng đã test OK.
- [x] **M2:** `Step`, `AutomationScript`, `ScriptSerializer` (JSON thụt lề, enum ghi dạng chữ, có `Version`, chịu được BOM, trường thiếu về mặc định), `ScriptRunner`, danh sách bước chọn nhiều, overlay chọn điểm, Open/Save/New, phím nóng F6 Start/Stop và Esc dừng khẩn cấp. Người dùng đã test, đã sửa lỗi layout.
- [x] **M3 (code):**
  - Core: `CoordinateMode` (Screen/Window), `WindowAnchor` (TopLeft, TopRight, BottomLeft, BottomRight, Center), `WindowTarget` (tên process, regex tiêu đề, BringToFront; lưu ở `AutomationScript.Target`), `WindowLocator` (tìm cửa sổ, vùng client, đổi qua lại màn hình/offset, đưa cửa sổ lên trước), `ScriptRunner` quy đổi lại vị trí cửa sổ ở mỗi lần chạy bước.
  - App: cột Position, chọn Position/Anchor cho từng bước, nhóm "Target window" (Process name, Title regex, Bring to front, "Pick window..." click vào cửa sổ để lấy tên process, "Check" kiểm tra tìm thấy cửa sổ). "Pick on screen..." tự đổi tọa độ thành offset khi bước ở chế độ Window.
  - Đã kiểm tra bằng cửa sổ test thật: tọa độ khớp từng pixel cho TopLeft/BottomRight/Center; dời cửa sổ rồi chạy lại vẫn trúng; báo lỗi rõ khi không thấy cửa sổ hoặc chưa đặt target; file JSON cũ đọc ra Screen/TopLeft. **Chưa test được giao diện** và `BringToFront` (best effort, phụ thuộc quy tắc foreground của Windows).

## Việc tiếp theo
- [ ] Người dùng test giao diện M3 (checklist bên dưới), báo lỗi nếu có.
- [ ] PR M3 vào `preview` khi người dùng yêu cầu.
- [ ] M4: gửi phím/text, nhận diện ảnh và điều kiện; M5: license (xem `docs/PROJECT.md`).

### Checklist test M3
1. Mở một chương trình (ví dụ Notepad), bấm "Pick window..." rồi click vào nó: ô Process name được điền; "Check" báo tìm thấy và kích thước vùng client.
2. Thêm bước, đặt Position = Window và Anchor **trước**, rồi bấm "Pick on screen..." chọn một điểm trong cửa sổ đó: X/Y là offset nhỏ (không phải tọa độ màn hình lớn). Click ngoài cửa sổ đích hoặc vào thanh tiêu đề thì phải báo lỗi, không lưu. Chọn Anchor sau khi đã pick sẽ giữ nguyên số X/Y nhưng đổi nghĩa, nên đổi Anchor xong phải Pick lại.
3. Chạy, rồi kéo cửa sổ sang chỗ khác và chạy lại: chuột vẫn trúng đúng điểm trong cửa sổ.
4. Đóng chương trình đích rồi Start: báo "Target window not found", không click lung tung.
5. Thử Anchor khác (BottomRight, Center): đặt Anchor rồi mới Pick; sau đó đổi kích thước cửa sổ và chạy lại.
6. Minimize chương trình đích: có Bring to front thì tự khôi phục, không có thì báo lỗi "minimized".
7. Lưu file rồi mở lại: Target và Position/Anchor giữ nguyên.
8. Thử ở scale hiển thị thật (125%/150%): báo nếu label/ô bị cắt (panel phải có thanh cuộn).

### Ghi chú cho milestone sau
- Trường mới có giá trị mặc định khác 0 phải được đặt trong `[OnDeserializing] SetDefaults` của `Step`/`AutomationScript`, vì serializer không chạy constructor/initializer; thiếu thì file cũ sẽ đọc ra 0. (Các trường M3 đều có mặc định 0 nên an toàn.)
- M4: `RegisterHotKey` có thể bắt cả phím do `SendInput` tạo ra, nên bước Key gửi F6/Esc có thể tự dừng kịch bản. Cần bỏ qua hotkey khi runner đang gửi phím, hoặc dùng tổ hợp có modifier làm phím dừng.
- Tọa độ Window-mode giả định cửa sổ giữ nguyên kích thước hoặc dùng anchor phù hợp; không tự co giãn theo kích thước.
- Khi target khớp nhiều cửa sổ (nhiều Notepad, nhiều workbook Excel...), lúc chạy runner lấy cửa sổ khớp đầu tiên theo thứ tự z-order (ưu tiên cửa sổ không minimize); cần điền Title (regex) để chỉ rõ cửa sổ nào. "Pick on screen..." thì dùng đúng cửa sổ được click và từ chối nếu nó không khớp target.

## Ghi chú môi trường
- Khi Claude chạy exe từ công cụ của nó, Windows có lúc báo "An Application Control policy has blocked this file" (trước đó), nhưng người dùng chạy trực tiếp thì bình thường, và về sau PowerShell của Claude nạp được DLL. Nguyên nhân chưa xác định; cần thử trên máy sạch trước khi phát hành.
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write thay vì `cat <<EOF`.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- Khi hoàn thành mỗi milestone, cập nhật file này.

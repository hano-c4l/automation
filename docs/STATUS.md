# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Giai đoạn tiếp theo đã code xong: **Keyboard, Command/Open, Control/Wait** (ngoài Mouse và Loop đã có). Build sạch, Core đã test bằng script với cửa sổ thật; **chờ người dùng test giao diện** (đặc biệt ô bắt phím).
- Nhánh: `feature_keyboard_wait` (cắt từ `feature_action_tree`, chưa push). `feature_action_tree` (cây action) và M3 cũng chưa vào `preview`; khi người dùng nói "PR into preview" cần PR cả chuỗi này (xem Git bên dưới).
- `preview` hiện có M1 + M2 (kịch bản dạng danh sách bước phẳng).

## Đã làm
- [x] M1, M2 (merge vào `preview`), M3 (tọa độ cửa sổ), cây action version 2 với Mouse + Loop (nhánh `feature_action_tree`).
- [x] **Keyboard:** KeyPress / KeyDown / KeyUp / ComboKey / Text. Phím lưu theo tên (`KeyCodes`: A-Z, 0-9, F1-F24, Num0-9, LeftCtrl/RightCtrl, LeftShift, LeftAlt..., Enter, Tab...). `KeyCaptureBox` bắt phím người dùng bấm: chế độ một phím chỉ lấy phím đầu, chế độ combo lấy các phím giữ cùng nhau theo thứ tự bấm. Text gõ bằng Unicode (`KEYEVENTF_UNICODE`) nên gõ được tiếng Việt, `\n` thành Enter, `\t` thành Tab. Time: KeyPress/Combo là thời gian giữ phím; Text là tổng thời gian gõ chia đều cho các ký tự (0 = nhanh nhất). Target (process + title regex): nếu đặt thì cửa sổ được đưa lên trước và nếu không lên được thì **không gửi phím** (báo lỗi), tránh gõ nhầm vào chương trình khác.
- [x] **Command/Open:** mở đường dẫn bằng shell (chương trình hoặc tài liệu). Cảnh báo: chạy file kịch bản của người khác sẽ mở bất cứ thứ gì trong đó.
- [x] **Control/Wait:** chờ cửa sổ khớp Process/Title xuất hiện và lên trước, rồi chạy các action con. Time = thời gian mỗi lượt chờ, Retry = số lượt thử thêm. Hết lượt mà không được thì dừng script với lỗi "Timed out" (chọn mặc định này vì spec chưa nói rõ).
- [x] **Phím dừng:** khi script chạy, F6/Esc dừng bằng hook bàn phím (`StopKeyHook`) bỏ qua phím do chính script gửi và không nuốt phím, nên script gửi F6/Esc cho chương trình khác được. Khi rảnh, F6 vẫn là hotkey toàn cục và được nhả ra khi đang bắt phím trong ô Key. Nếu không cài được hook thì quay về `RegisterHotKey`.
- [x] Test script: round-trip JSON (Wait + con, Command, Text có xuống dòng), gõ chuỗi tiếng Việt vào cửa sổ test (từng ký tự khớp), KeyDown/KeyUp Shift + KeyPress, Combo, thời gian giữ phím (300 ms đo 310 ms), Wait thành công/hết hạn (retry hoạt động), tên phím sai và cửa sổ đích không có đều báo lỗi mà không gõ gì, Open chương trình thật và đường dẫn sai. Đã chụp ảnh kiểm tra bố cục các form Keyboard/Command/Wait.

## Việc tiếp theo
- [ ] Người dùng test giao diện (checklist dưới), báo lỗi nếu có.
- [ ] PR vào `preview` khi người dùng yêu cầu.
- [ ] Image (nhận diện ảnh), Record; License (M5).

### Checklist test giao diện (giai đoạn Keyboard / Open / Wait)
1. Category có đủ Control, Command, Mouse, Keyboard; mỗi loại hiện đúng form, không bị cắt.
2. Keyboard, Action = KeyPress: bấm vào ô Key rồi nhấn một phím (thử Left Ctrl, F2, Tab, mũi tên, F6): ô hiện tên phím đầu tiên; nhấn Ctrl rồi A cùng lúc vẫn chỉ là LeftCtrl. Lúc đang bắt phím F6 không được khởi động script.
3. Action = ComboKey: giữ Ctrl, Shift rồi bấm S: ô hiện LeftCtrl+LeftShift+S; nhả hết rồi bấm tổ hợp mới thì thay hoàn toàn.
4. Action = Text: ô Text nhiều dòng, dán được đoạn văn tiếng Việt; Add rồi Update giữ nguyên nội dung. Time chỉ hiện với KeyPress / ComboKey / Text.
5. Chạy script gồm Keyboard Text với Target = Notepad: Notepad được đưa lên trước và chữ (kể cả tiếng Việt, xuống dòng) được gõ vào. Thử không đặt Target: gõ vào cửa sổ đang có focus. Thử script gửi phím F6 hoặc Esc cho Notepad: không tự dừng script.
6. Dừng script giữa chừng bằng F6 và bằng Esc (bằng tay): dừng ngay, phím đang giữ được nhả.
7. Command/Open: nhập đường dẫn hoặc Browse, chạy: chương trình mở lên; đường dẫn sai thì báo lỗi.
8. Control/Wait: tạo Wait (Process = notepad) có action con (dùng Add khi đang chọn Wait, hoặc In). Chạy khi chưa mở Notepad rồi mở Notepad trong thời gian chờ: action con chạy. Để hết Time: báo "Timed out waiting", script dừng. Thử Retry > 0.
9. Save rồi Open lại: mọi action Keyboard / Command / Wait và cây con giữ nguyên.

### Ghi chú cho milestone sau
- Trường mới có giá trị mặc định khác 0 phải đặt trong `[OnDeserializing] SetDefaults` của lớp tương ứng và không dùng `EmitDefaultValue=false` cho chúng (giá trị 0 do người dùng đặt sẽ bị mất khi nạp lại). Các sub-object (Mouse/Keyboard/Command/Loop/Wait) là tham chiếu nên dùng `EmitDefaultValue=false` an toàn.
- Image sẽ đặt `RunContext` mới cho các action con (cửa sổ + vị trí ảnh làm gốc); action con của Image kế thừa Process của Image theo spec.
- Khi Process khớp nhiều cửa sổ, runner lấy cửa sổ khớp đầu tiên theo z-order (ưu tiên không minimize); cần điền Title (regex) để chỉ rõ.
- Đổi Anchor sau khi pick sẽ giữ nguyên số X/Y nhưng đổi nghĩa; phải pick lại.
- Multi-select giữ bằng ListView thụt lề (TreeView của WinForms không chọn nhiều được). Chưa có thu gọn/mở rộng nhánh.
- Delay ngắn (<20 ms) và mỗi vòng lặp không delay dùng chờ có nhường CPU (không dùng timer) vì Windows 11 bỏ qua `timeBeginPeriod` khi app thu nhỏ; loop zero-delay chạy hết tốc độ một lõi CPU.
- Phím Win không bắt được trong ô Key (hệ thống chặn); gõ Text không đi qua IME nên Unikey/EVKey không làm méo.
- Game hoặc chương trình chạy admin: `SendInput` bị UIPI chặn nếu app không chạy admin (chưa có tùy chọn chạy admin).

### Git
- Chuỗi nhánh chưa vào `preview`: `feature_m3_window_coords` -> `feature_action_tree` -> `feature_keyboard_wait` (mỗi nhánh cắt từ nhánh trước). Một PR từ `feature_keyboard_wait` vào `preview` gồm tất cả.

## Ghi chú môi trường
- Khi Claude chạy exe từ công cụ của nó, Windows có lúc báo "An Application Control policy has blocked this file", nhưng người dùng chạy trực tiếp thì bình thường. Nguyên nhân chưa xác định; cần thử trên máy sạch trước khi phát hành.
- Build lỗi "file is being used" khi exe đang chạy: tắt app trước khi build (hoặc build ra thư mục khác bằng `-p:OutDir=`).
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write thay vì `cat <<EOF`.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- `.claude/launch.json` còn trỏ tới server npm "client-dev" không thuộc dự án này; cần sửa hoặc xóa.

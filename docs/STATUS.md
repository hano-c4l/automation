# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Nhánh `feature_name_columns` (cắt từ `preview` sau PR #3, chưa push) gồm hai phần: (1) chỉnh phần cũ theo yêu cầu: field **Name**, bỏ Target của Keyboard, đổi cột danh sách, menu Open/Save/Save As; (2) action mới **Control/Image** (nhận diện ảnh). Build sạch, Core và UI đã test bằng harness C#; **chờ người dùng test thao tác tay trên giao diện** (đặc biệt kéo chọn vùng, chụp ảnh, pick vị trí trong Image).
- `preview` đã có: M1-M3, cây action, Keyboard, Open, Wait (PR #3).

## Đã làm
- [x] M1, M2, M3, cây action, Mouse/Loop, Keyboard, Command/Open, Control/Wait (đã vào `preview`).
- [x] **Name:** mọi action có alias tùy chọn (`ActionNode.Name`). Cột Name đứng đầu danh sách, thụt lề theo cấp; để trống thì hiện loại action. Ô Name được xóa sau mỗi lần Add.
- [x] **Keyboard không còn Target/Process** (không phụ thuộc tọa độ): phím gửi tới cửa sổ đang có focus. Muốn gửi tới chương trình cụ thể thì đặt một Click hoặc Wait ngay trước. File cũ có trường Target sẽ bị bỏ qua khi nạp.
- [x] **Danh sách:** các cột Name | Action | Process | Details | Time | Delay. Process tách riêng (trống nếu action không có), Details chỉ còn thông tin cơ bản và được kéo dài theo bề rộng còn lại.
- [x] **Menu:** New, Open, Save (ghi thẳng vào file hiện tại, chưa có file thì hỏi đường dẫn), Save As.
- [x] **Control/Image:** Process + Anchor + Range (X, Y, rộng, cao; 0 = cả vùng client hoặc cả màn hình khi không có Process) + ảnh mẫu + Accuracy (%, mặc định 90). Nếu tìm thấy thì chạy các action con; không thấy thì bỏ qua con, báo "Image not found (best match N%)" và script chạy tiếp. Action Mouse con kế thừa Process của Image (ô Process bị khóa), Anchor/X/Y tính theo hình chữ nhật nơi ảnh được tìm thấy (ví dụ ảnh ở (100,200) trong cửa sổ, TopLeft (10,20) thì click tại (110,220) theo cửa sổ). Nút: Capture picture (kéo chọn vùng màn hình), Load file, Pick range, Find now (cho biết vị trí và % khớp), Pick position trong Image (ảnh phải đang hiện trên màn hình).
  - Các lần kéo/click trên overlay (Pick position trong Image với Anchor TopLeft/Center, Capture picture, Pick range có Process, Find now) đã được thử bằng chuột giả lập trong harness và đều đúng; tìm toàn desktop 1920x1080 thực mất ~50 ms. Khi ảnh xuất hiện nhiều lần thì lấy kết quả khớp tốt nhất.
  - Thuật toán: ZNCC (so khớp mẫu chuẩn hóa trừ trung bình) trên ảnh xám, tìm thô trên ảnh thu nhỏ 2-4 lần rồi tinh chỉnh ở độ phân giải đầy đủ; thêm kiểm tra màu trung bình để loại nút cùng hình khác màu. Ảnh mẫu gần như một màu bị từ chối khi chụp.
  - Ảnh lưu trong chính file script (pixel nén Deflate + base64), Core không phụ thuộc System.Drawing (chụp màn hình bằng GDI).
  - Test harness: nút trống không khớp nút có chữ (decoy); bản sáng hơn + nhiễu vẫn khớp (~60 ms cho ảnh 1920x1080); cùng hình khác màu không khớp; ảnh mẫu nhỏ; ảnh phẳng bị từ chối; dữ liệu ảnh hỏng báo lỗi; JSON round-trip; cửa sổ thật: ví dụ (100,200)+(10,20) trúng (110,220), Center, BottomRight, vùng tìm có/không chứa ảnh, ảnh biến mất, cửa sổ không tồn tại, tìm trên desktop không Process. Giao diện: Add không ảnh bị từ chối, ảnh phẳng bị từ chối, thêm con vào trong Image, Process bị khóa, pick vị trí khi ảnh không hiện cho thông báo rõ, lưu/mở lại.

## Việc tiếp theo
- [ ] Người dùng test giao diện (checklist dưới), báo lỗi nếu có.
- [ ] PR vào `preview` khi người dùng yêu cầu.
- [ ] Record; License (M5).

### Checklist test giao diện (Name, cột, menu, Image)
1. Cột danh sách: Name, Action, Process, Details, Time, Delay; Details rộng; Name để trống thì hiện loại action; đặt Name thì hiện alias, thụt lề theo cây.
2. Keyboard không còn ô Target.
3. Save lưu thẳng vào file đang mở (không hỏi); Save As hỏi chỗ mới; file mới chưa lưu thì Save hỏi đường dẫn.
4. Image: Category Control, Type Image. "Capture picture..." kéo chọn một nút/biểu tượng trên màn hình: ảnh hiện trong ô Picture; chọn vùng gần như một màu thì bị từ chối. Add.
5. "Find now" (khi ảnh đang hiện trên màn hình): báo vị trí và % khớp; che ảnh đi rồi bấm lại: báo Not found và best match.
6. Đặt Process cho Image, đặt Anchor, "Pick range..." kéo vùng trong cửa sổ đó; Width/Height/X/Y được điền (X/Y là offset theo Anchor). Thử Width/Height = 0 để tìm cả cửa sổ.
7. Chọn Image rồi thêm action Mouse (Add khi đang chọn Image): nằm trong Image, ô Process bị khóa. "Pick position..." lúc ảnh đang hiện: X/Y là offset so với góc/tâm ảnh theo Anchor. Chạy script: chuột đi tới đúng điểm đó kể cả khi ảnh đã dời chỗ trên màn hình/cửa sổ.
8. Chạy khi ảnh không có trên màn hình: action con bị bỏ qua, thanh trạng thái báo "Image not found (best match ...)", script tiếp tục.
9. Save rồi Open lại: ảnh, vùng, Accuracy, Name và con giữ nguyên.
10. Chuyển một action Mouse vào hoặc ra khỏi Image bằng In/Out làm đổi nghĩa X/Y (tính theo ảnh hay theo cửa sổ/màn hình): cần Pick lại. Trong danh sách, action nằm trong Image hiện "(picture)" ở cột Process và luôn có Anchor ở Details.
11. Capture picture: sau khi thả chuột, con trỏ được dời ra ngoài vùng trước khi chụp nên nút không bị chụp ở trạng thái hover.

### Ghi chú cho milestone sau
- Trường mới có giá trị mặc định khác 0 phải đặt trong `[OnDeserializing] SetDefaults` của lớp tương ứng và không dùng `EmitDefaultValue=false` cho chúng. Các sub-object (Mouse/Keyboard/Command/Loop/Wait/Image) là tham chiếu nên dùng `EmitDefaultValue=false` an toàn.
- `ImageLocator.Locate` là điểm duy nhất biến cài đặt Image thành phép tìm trên màn hình (runner, Find now, Pick position trong Image đều dùng).
- Khi Process khớp nhiều cửa sổ, runner lấy cửa sổ khớp đầu tiên theo z-order (ưu tiên không minimize); cần điền Title (regex) để chỉ rõ.
- Đổi Anchor sau khi pick sẽ giữ nguyên số X/Y nhưng đổi nghĩa; phải pick lại.
- Multi-select giữ bằng ListView thụt lề (TreeView của WinForms không chọn nhiều được). Chưa có thu gọn/mở rộng nhánh.
- Delay ngắn (<20 ms) và mỗi vòng lặp không delay dùng chờ có nhường CPU (không dùng timer) vì Windows 11 bỏ qua `timeBeginPeriod` khi app thu nhỏ.
- Phím Win không bắt được trong ô Key. Gõ Text dùng Unicode packet; chưa thử khi Unikey/EVKey đang bật.
- Chụp màn hình bằng BitBlt có thể ra màu đen với nội dung DirectX/toàn màn hình độc quyền (game). Accuracy là ngưỡng giống nhau, không xử lý ảnh mẫu bị che một phần.
- Game hoặc chương trình chạy admin: `SendInput` bị UIPI chặn nếu app không chạy admin (chưa có tùy chọn chạy admin).

## Ghi chú môi trường
- PowerShell của Claude nạp DLL vừa build bị Application Control chặn (lỗi 0x800711C7); chạy test bằng harness C# (`dotnet run` project trong thư mục scratchpad, tham chiếu Core/App) thì chạy được. Dùng harness, không tìm cách lách chính sách.
- Khi Claude chạy exe từ công cụ của nó, Windows có lúc báo "An Application Control policy has blocked this file", nhưng người dùng chạy trực tiếp thì bình thường. Nguyên nhân chưa xác định; cần thử trên máy sạch trước khi phát hành.
- Build lỗi "file is being used" khi exe đang chạy: tắt app trước khi build (hoặc build ra thư mục khác bằng `-p:OutDir=`).
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write thay vì `cat <<EOF`.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- `.claude/launch.json` còn trỏ tới server npm "client-dev" không thuộc dự án này; cần sửa hoặc xóa.

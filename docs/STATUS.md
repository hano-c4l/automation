# Status

Cập nhật lần cuối: 2026-11-06

## Hiện tại
- Tất cả tính năng đã chốt đều đã code và vào `preview` (PR #1–#6), **trừ License (M5)**. Chương trình chạy được đầy đủ: Mouse, Keyboard, Command/Open, Loop, Wait, Image, Variable, Condition, biểu thức, Record, lưu/mở file.
- Nhánh đang làm: `feature_docs_update` (chỉ cập nhật tài liệu).
- Việc còn lại: **License (M5)** và công cụ KeyGen; test tay trên giao diện thật (xem checklist); ký số và kích hoạt online để sau.

## Những gì đã có

### Chương trình và file
- Solution: `src/Automation.Core` (netstandard2.0: model, JSON, engine chuột/phím, so khớp ảnh, biểu thức, chuyển bản ghi thành action, runner) và `src/Automation.App` (net48 WinForms). .NET SDK 10.0.403 để build; Per-Monitor V2 DPI.
- File kịch bản JSON `Version` 2: cây action; mọi thời gian bằng mili giây; enum lưu bằng tên; ảnh mẫu nằm trong file (pixel nén Deflate + base64). File version 1 bị từ chối với thông báo rõ.
- Thanh menu: New, Open, Save, Save As (chỉ hiện khi đã có file); **Start/Stop (F6)** và **Record (F7)** nằm giữa phần thanh bên trái; 7 nút icon (Add, Update, Delete, In, Out, Up, Down) cùng hàng, canh trái thẳng với phần Action detail. Add luôn bật; Update chỉ bật khi chọn đúng một dòng; các nút còn lại cần chọn ít nhất một dòng (dòng End không tính).
- New, Open và đóng chương trình hỏi "lưu trước khi ...?" (Yes/No/Cancel) khi có thay đổi chưa lưu: Yes lưu vào file hiện tại (hoặc hiện hộp Save nếu chưa có; hủy hộp Save thì hủy luôn thao tác), No bỏ qua, Cancel ở lại (so sánh JSON hiện tại với JSON lúc mở/lưu/new; không có file mà có dữ liệu cũng tính là chưa lưu).
- Danh sách action (trái, chọn nhiều được, thụt lề theo cây): Name | Action | Process | Details | Time | Delay. Name trống thì hiện loại action. Condition hiện `Tên - Begin`, các case, `else`, `Tên - End`.
- Panel Action detail (phải, 475 px): Name, Category, Action (combo chung ngoài group box) rồi form theo loại. Mọi ô nhập rộng 200 px; đơn vị nằm trong nhãn (Position (px), Accuracy (%), Retry (times), Scroll (notches)); nút Pick/Check/Capture/Browse và dấu ? chung một cột ngay sau ô (hàng có nút thì ? sau nút); ô chữ có gợi ý mờ (`HintTextBox`, `NumberBox` cho số nguyên); dấu **?** mở cửa sổ giải thích (biểu thức, Title regex, Times, Anchor, Range, ID, Accuracy, Wait/Retry, Time/Delay, Value type...).

### Action
- **Mouse:** Click, MouseUp, MouseDown, Move (không có Type và không lưu Button), ScrollUp/ScrollDown (nút giữa, số nấc). Time = thời gian di chuột tới vị trí; Delay = chờ sau action. Process (tên + regex tiêu đề) và Anchor (TopLeft, TopRight, BottomLeft, BottomRight, Center); số âm = nằm trong cửa sổ khi neo phải/dưới. Position nhập tay (kèm Pick) hoặc từ biểu thức (`{img.Position} + {10,20}`, `{p.X + 1}, {p.Y + 1}`). Khi có Process: cửa sổ được đưa lên trước, và dừng với lỗi nếu điểm bị cửa sổ khác che.
- **Keyboard:** KeyPress/KeyUp/KeyDown/ComboKey (ô bắt phím), Text (Unicode nên gõ được tiếng Việt; nhập tay hoặc biểu thức). Gửi tới cửa sổ đang focus (không có Target; đặt Click hoặc Wait trước). Phím chưa nhả được nhả khi dừng.
- **Command/Open:** mở đường dẫn bằng shell. Chạy file kịch bản của người khác sẽ mở bất cứ thứ gì trong đó.
- **Control/Loop:** Type = Times (0 = vô hạn), Duration hoặc Until (kiểm tra giữa các vòng). **Wait:** chờ cửa sổ khớp Process/Title xuất hiện và lên trước (Wait, Retry), hết hạn thì dừng với lỗi.
- **Control/Image:** ID, Process, Anchor (một anchor duy nhất cho vùng tìm và Position trả về), Range (X, Y, Width, Height; Width và Height cùng 0 = tìm toàn vùng), ảnh mẫu (Capture kéo chọn trên ảnh chụp đóng băng; Pick chọn file), Accuracy (ZNCC), Check. Thấy ảnh thì chạy action con; không thấy thì bỏ qua con và chạy tiếp. Biến ID lưu **Found, Position, Size**; Position là góc ảnh tương ứng với Anchor đo từ cùng góc cửa sổ (không có Process: tọa độ màn hình của góc trên-trái). Ví dụ (cửa sổ 700x700, vùng 200x200, ảnh 100x100 ở giữa): TopLeft, vùng (100,100) -> (150,150); BottomRight, vùng (-400,-400) -> (-450,-450). Mouse cần cùng Process và Anchor để trúng góc đó (neo phải/dưới thì Position là điểm ngay sau mép ảnh; cộng `{-1,-1}` hoặc dùng Center để click vào ảnh).
- **Control/Variable:** ID (chữ, số, `_`; không bắt đầu bằng số; không phải true/false/now/today; không phân biệt hoa thường; chung không gian tên với ID của Image), kiểu Position, Range, String, Number, Boolean, Date, DateTime; giá trị nhập hoặc biểu thức. Position/Range chỉ chứa số (không Process/Anchor; Pick cho tọa độ màn hình). Khai báo lại cùng ID là gán lại, kiểu của lần sau thắng. Biến toàn cục trong một lần chạy.
- **Control/Condition (switch-case):** có sẵn một Case (biểu thức nhập ở ô Condition, bắt buộc) và `else`; chỉ chạy case đầu tiên đúng, không có thì chạy else. Action = Case chỉ có khi đang chọn một dòng thuộc Condition; không cho hai case cùng biểu thức (bỏ qua khoảng trắng ngoài dấu nháy, phân biệt hoa thường). Quy tắc: không Add trực tiếp vào Condition (chỉ vào case/else); không xóa else và case cuối; case chỉ Up/Down giữa các case, không vượt else, không In/Out; Out từ trong case/else đặt ngay sau Condition; Update Condition đổi tên và case đầu; file sai cấu trúc được sửa khi nạp.
- **Biểu thức** (`Automation.Core.Expressions`): `{abc}`, bên trong `{ }` được viết tên trần và thuộc tính (`{img.Position.X + 1}`); `{1,1}` là Position, `{x,y,w,h}` là Range; toán tử `+ - * / %`, `= == != < <= > >=`, `&& || !` (ngắn mạch), ngoặc; chuỗi `"..."` hoặc `'...'`; `true false now today`; `+` với chuỗi là nối; Date/DateTime cộng số = cộng ngày; Position cộng trừ nhau. Thuộc tính: Position (X, Y), Size (Width, Height), Range (X, Y, Width, Height, Position, Size), Image (Found, Position, Size). Số/ngày luôn theo invariant culture (máy vi-VN dùng dấu phẩy thập phân). Biến chưa có giá trị dừng script và nêu tên biến; lỗi cú pháp báo vị trí.
- **Record (F7):** ghi bằng hook bàn phím và chuột cấp thấp (thao tác vẫn đến chương trình). Bấm/nhả chuột cùng chỗ = Click; nhả ở chỗ khác (hoặc đã rê xa rồi quay lại) = MouseDown + các Move theo đường đi (lấy mẫu 8 ms rồi lọc bớt điểm nằm trong 1 px so với đường nối; vòng tròn bán kính 100 px trong 3 giây ≈ 30 dòng Move) + MouseUp; các nấc lăn liền nhau cùng chỗ = một Scroll; phím = Key press (Time = thời gian giữ); từ 2 phím gõ ký tự liền nhau (chữ, số, dấu cách, cả Shift+ký tự; cách nhau tối đa 1,5 giây, theo bố cục bàn phím hiện tại) gộp thành một action Text (Time = thời gian gõ); phím bấm khi giữ Ctrl/Alt/Shift/Win = Combo key; khoảng nghỉ thành Delay (trừ 100 ms rê chuột). Tọa độ màn hình (không Process). Ô "All event" cạnh nút Record (mặc định tắt): bật thì ghi cả di chuyển chuột khi không giữ nút (thành các Move, cũng lọc bớt điểm); tắt thì không ghi di chuyển đơn thuần. Không ghi: thao tác trên chính cửa sổ chương trình, F6, F7, phím không có tên (Esc có được ghi). Kết quả chèn như Add (vào Loop/Wait/Image/case/else đang chọn, sau dòng đang chọn, hoặc cuối danh sách).
- **Khóa:** khi đang Start hoặc Record, mọi nút và ô bị khóa; chỉ còn Stop của việc đang làm. Khi script chạy, F6 và F7 được nhả khỏi hotkey, F6/Esc dừng script qua hook bỏ qua phím do script tự gửi, nên script gửi được F6/F7/Esc cho chương trình khác. Đóng chương trình khi đang ghi: dừng ghi trước rồi hỏi xác nhận.

### Sửa lỗi đáng nhớ
- Con trỏ chuột từng lệch 1 px do làm tròn khi quy ra tọa độ 0..65535; đã sửa (test quét 400 điểm + 4 góc).
- `Placeholder` không có trên `TextBox` của .NET Framework: dùng `HintTextBox`.

## Kiểm thử
- Harness C# trong thư mục scratchpad của phiên làm việc (không nằm trong repo): project `net48` tham chiếu Core/App, chạy bằng `dotnet run`. Gồm: ExprTests (biểu thức, vi-VN), VarTests (runner: Variable/Condition/Loop, Image trên cửa sổ thật gồm ví dụ 700x700 với cả năm anchor), RecordTests (bộ chuyển đổi), UiTests (soạn qua giao diện, mọi quy tắc cây, căn hàng/độ rộng, ô số, placeholder, hotkey, khóa, ghi qua hook thật bằng thao tác giả lập, overlay Pick/Capture bằng chuột giả lập). Lần chạy gần nhất: tất cả qua, trừ một lần test con trỏ-trúng-pixel lệch do chuột thật bị chạm lúc test (chạy lại thì qua).
- Chưa kiểm: giao diện ở 125%/150% (chỉ chạy ở 96 DPI), nhiều màn hình có DPI khác nhau, Unikey/EVKey đang bật khi gõ Text, game/DirectX (BitBlt có thể ra màu đen), chương trình chạy admin (UIPI).

### Checklist test tay (giao diện thật)
1. Mở app: thanh menu có New, Open, Save, Start/Stop và Record ở giữa; icon bên phải canh trái với phần Action detail; chưa chọn gì thì chỉ Add đậm; xem ở 125%/150% không bị cắt.
2. Mọi trạng thái (Mouse Input/Variable, Keyboard Key/Text, Command, Loop 3 kiểu, Wait, Image, Variable mỗi kiểu): ô rộng đều, mép phải thẳng, nút Pick cùng cột, dấu ? thẳng hàng, không bị cắt hay cuộn ngang.
3. Dấu ? (Times, Title, Anchor, Expression, ID, Accuracy...): cửa sổ giải thích đúng nội dung, đóng bằng Close/Esc.
4. New khi có dữ liệu chưa lưu: hỏi Yes/No/Cancel: Yes lưu rồi New, No New luôn, Cancel giữ nguyên. Mở/lưu file rồi New khi chưa sửa: không hỏi; sửa rồi: hỏi và nêu tên file. Open và đóng chương trình cũng hỏi.
5. Variable: ID sai bị từ chối; biểu thức lỗi báo vị trí; thử mọi kiểu. Condition: tạo (ô Condition bắt buộc), thêm case (Action = Case chỉ có khi chọn dòng thuộc Condition), case trùng bị từ chối, các quy tắc xóa/di chuyển/In/Out. Chạy `abc = 5` với `{abc} > 3` rồi `{abc} > 2`: chỉ case đầu chạy.
6. Image: Capture khi màn hình đang thay đổi (ảnh đúng với lúc khoanh); Check báo vị trí và %; Pick range với Process và Anchor; đặt ID rồi Mouse `{img.Position}` cùng Process và Anchor trúng đúng góc ảnh, dời cửa sổ vẫn trúng; Condition dùng `{img.Found}`.
7. Mouse, Action = Move: không có Type, file không có "Button". Update chỉ bật khi chọn đúng một dòng.
8. Keyboard: bắt phím (kể cả F6, Tab, mũi tên; F6 khi đang bắt không khởi động script); Text từ biểu thức; script gửi F6/F7/Esc cho Notepad không tự dừng.
9. Record: bấm Record (hoặc F7), click, kéo, lăn chuột, gõ phím, Ctrl+C, rồi F7: các action xuất hiện đúng chỗ (thử đang chọn Loop, đang chọn một dòng, không chọn gì); trong lúc ghi mọi nút/ô bị khóa, chỉ Stop recording bật; chạy lại bản ghi cho kết quả giống.
10. Start: mọi thứ bị khóa trừ Stop; F6/Esc dừng được; Save rồi Open lại giữ nguyên toàn bộ (biến, condition, Image, ảnh).

## Ghi chú kỹ thuật
- Trường mới có giá trị mặc định khác 0 phải đặt trong `[OnDeserializing] SetDefaults` của lớp tương ứng và không dùng `EmitDefaultValue=false` cho chúng. Các sub-object (Mouse/Keyboard/Command/Loop/Wait/Image/Variable/Case) là tham chiếu nên dùng `EmitDefaultValue=false` an toàn. Trường bị bỏ khỏi model thì file cũ chứa nó vẫn mở được (bỏ qua).
- `ImageLocator.Locate` là điểm duy nhất biến cài đặt Image thành phép tìm trên màn hình (runner, Check đều dùng).
- Khi Process khớp nhiều cửa sổ, runner lấy cửa sổ khớp đầu tiên theo z-order (ưu tiên không minimize); điền Title (regex) để chỉ rõ.
- Đổi Anchor sau khi pick giữ nguyên số nhưng đổi nghĩa; pick lại.
- Danh sách dùng ListView thụt lề (TreeView của WinForms không chọn nhiều được); có mũi tên ▼/▶ trước tên action có con: bấm vào mũi tên, hoặc phím Left/Right, để thu gọn/mở rộng; nút Collapse All và Expand All trên menu bar; thêm action vào nhánh đang thu gọn sẽ tự mở nhánh. Trạng thái thu gọn không lưu vào file.
- Delay ngắn (<20 ms) và vòng lặp không delay dùng chờ có nhường CPU (Windows 11 bỏ qua `timeBeginPeriod` khi app thu nhỏ).
- Ô bắt phím dùng hook bàn phím cấp thấp khi đang focus (nuốt phím), nên bắt được Alt+Tab; phím Win có thể bắt được nhưng chưa kiểm.
- Pick process: rê chuột thì có viền đỏ (2 px) quanh cửa sổ đang chỉ; desktop, taskbar, Start/Search là "full desktop" (viền đỏ cả màn hình, Process để trống).
- Khi Record, chương trình tự thu nhỏ; dừng bằng F7. Bấm vào taskbar để mở lại chương trình sẽ bị ghi thành một click.

## Ghi chú môi trường
- PowerShell của Claude nạp DLL vừa build bị Application Control chặn (0x800711C7), và đôi khi chính exe test mới cũng bị chặn tạm thời; chạy lại sau ít phút thì được. Dùng harness `dotnet run`, không tìm cách lách chính sách. Người dùng chạy exe trực tiếp thì bình thường; cần thử trên máy sạch trước khi phát hành.
- Build lỗi "file is being used" khi exe đang chạy: tắt app trước khi build (hoặc build ra thư mục khác bằng `-p:OutDir=`).
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write.

## Mở / lưu ý
- License (M5): Machine ID, file license ký ECDSA, KeyGen riêng, chống lùi đồng hồ (xem `docs/PROJECT.md`); nút nhập License sẽ nằm trên thanh menu.
- Ký số chưa quyết định; kích hoạt license online để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- Chưa có công cụ chuyển đổi file cũ khi đổi ngữ nghĩa Anchor của Image (vùng tìm của Image có Process, anchor khác TopLeft và có đặt vùng sẽ dịch chỗ) hoặc file version 1.

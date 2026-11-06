# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Nhánh `feature_ui_polish` (cắt từ `preview` sau PR #4, chưa push): chỉnh giao diện theo yêu cầu (xem mục "Giao diện" bên dưới). Build sạch; UI test bằng harness qua. **Chờ người dùng xem/test thao tác tay.**
- `preview` đã có tất cả đến Variable/Condition (PR #3, #4).

## Đã làm
- [x] M1, M2, M3, cây action, Mouse/Loop, Keyboard, Command/Open, Control/Wait (đã vào `preview`).
- [x] Name, bỏ Target Keyboard, cột Name|Action|Process|Details|Time|Delay, menu Open/Save/Save As; Control/Image (nhánh `feature_name_columns`).
- [x] **Biểu thức** (`Automation.Core.Expressions`): biến viết `{abc}`; trong `{ }` được viết tên trần và thuộc tính (`{img.Position.X + 1}`); `{1,1}` là Position, `{x,y,w,h}` là Range. Toán tử `+ - * / %`, `= == != < <= > >=`, `&& || !` (ngắn mạch), ngoặc tròn; chuỗi trong `"..."` hoặc `'...'`; `true false now today`. Số/ngày luôn theo invariant culture (máy vi-VN dùng dấu phẩy thập phân nên không dựa vào culture hiện tại). `+` với chuỗi là nối chuỗi; Date/DateTime `+ số` = cộng ngày, trừ nhau ra số ngày; Position cộng/trừ nhau, nhân/chia số. Thuộc tính: Position (X, Y), Size (Width, Height), Range (X, Y, Width, Height, Position, Size), Image (Found, Position, Size). Biến chưa có giá trị -> dừng script và nêu tên biến; lỗi cú pháp báo vị trí. Chỉ thấy tên trần ngoài `{ }` thì báo "write {name}".
- [x] **Variable** (Control): ID (chữ, số, `_`; không bắt đầu bằng số; không được là true/false/now/today; không phân biệt hoa thường; dùng chung một không gian tên với ID của Image), kiểu Position / Range / String / Number / Boolean / Date / DateTime, giá trị nhập (có picker cho Position và Range, kèm Process + Anchor để quy đổi) hoặc biểu thức. Khai báo lại cùng ID là gán lại và **kiểu của lần khai báo sau thắng** (giá trị được ép sang kiểu đó, không ép được thì dừng với lỗi). Số nhập dạng `1,5` hay `1.5` đều được và lưu bằng dấu chấm.
- [x] **Image có ID:** biến ID lưu **Found** và, khi thấy ảnh, **Position** và **Size**. Image chỉ có **một Anchor** (dùng khi có Process): vùng tìm đặt góc tương ứng của nó tại (Position X, Y) tính từ cùng góc của vùng client cửa sổ (Center thì tính từ tâm), và Position của ảnh trả về là góc tương ứng của ảnh đo theo cùng cách. Ví dụ (cửa sổ 700x700, vùng 200x200, ảnh 100x100 ở giữa vùng): TopLeft, vùng ở (100,100) -> ảnh (150,150); BottomRight, vùng ở (-400,-400) -> ảnh (-450,-450) (số âm = nằm trong cửa sổ, đo từ góc dưới-phải; người dùng chọn giữ số âm). Không có Process thì Position là tọa độ màn hình của góc trên-trái và Anchor không có tác dụng. Mouse chỉ dùng Anchor của chính nó với Process của nó (cần cùng Process và Anchor như Image để trúng góc đó), không cần biết Anchor của Image. Không thấy ảnh: `Found` = false (đọc Position/Size thì báo lỗi), action con bị bỏ qua, script tiếp tục. Width và Height cùng 0 = tìm toàn vùng client / desktop (Position bị bỏ qua); đặt một trong hai bằng 0 mà cái kia > 0 bị từ chối khi Add/Update.
- [x] **Variable chỉ chứa giá trị:** Position/Range không còn Process, Title, Anchor (file cũ có các trường này vẫn mở, bị bỏ qua); Pick cho tọa độ màn hình. Việc giá trị đo từ cửa sổ/anchor nào do action dùng nó (Mouse) quyết định. Bỏ luôn "Picture anchor" của Image.
- [x] **Mouse** có "Position from": Input (X/Y + picker) hoặc Variable (biểu thức, ví dụ `{img.Position} + {10,20}` hoặc `{img.Position.X + 1}, {img.Position.Y + 1}`). Mouse trong Image **không còn kế thừa** Process hay tọa độ của ảnh (file cũ có con của Image sẽ tính theo cửa sổ/màn hình).
- [x] **Keyboard Text** có "Text from": Input hoặc Variable (biểu thức, kết quả đổi sang chuỗi: số nguyên bỏ phần thập phân, ngày `yyyy-MM-dd`, Position `x,y`).
- [x] **Condition (switch-case):** tạo ra là có sẵn 1 Case (biểu thức nhập ở ô First case, bắt buộc; `true` chỉ dùng khi sửa file hỏng) và `else`; danh sách hiện `Tên - Begin`, các case (tên alias hoặc biểu thức), `else`, `Tên - End`. Chỉ chạy case đầu tiên đúng, không case nào đúng thì chạy else. Thêm case: Type = Case khi đang chọn Condition (hoặc một case), case mới chèn trước else. Quy tắc: không Add action trực tiếp vào Condition (chỉ vào case/else); không xóa else; không xóa case cuối cùng (xóa cả Condition); case chỉ Up/Down giữa các case và không vượt qua else; case/else không In/Out; Out từ trong case/else đặt action ngay sau Condition; Condition/Case không đổi sang kiểu khác qua Update. Dòng End chỉ là dòng hiển thị, không chọn/xóa được. File sai cấu trúc được sửa khi nạp (ít nhất 1 case, đúng 1 else ở cuối).
- [x] **Save As** chỉ hiện khi đang mở từ file hoặc đã lưu.
- [x] **Sửa lỗi lệch 1 pixel:** `MouseController` làm tròn xuống khi quy đổi tọa độ ra 0..65535 nên đôi khi trỏ chuột lệch 1 px (thấy ở tọa độ nhỏ). Đã đổi sang làm tròn lên theo công thức Windows dùng; test quét 400 điểm ngẫu nhiên + 4 góc màn hình đều trúng đúng pixel.
- [x] **Test:** ExprTests (bộ ví dụ của người dùng: `{abc} + 1`, `{abc} > 3`/`> 2`/else với abc=5 chỉ chạy case đầu, `{abc.Position.X + 1}, ...`, `{abc.Position} + {1,1}`; ưu tiên toán tử, ngắn mạch, chia 0, chuỗi có escape, ngày, biến chưa có, vị trí lỗi cú pháp, vi-VN), VarTests (runner: switch 5/3/2, vòng lặp 5 lần có Condition bên trong đếm đúng, mọi kiểu literal, Position/Range, Mouse dùng biến, Text từ biểu thức, ID/kiểu sai, JSON round trip và sửa Condition hỏng, Image: (110,220), Center, BottomRight (-600,-300), Found/not found, desktop), UiTests (soạn Variable/Condition qua giao diện, mọi quy tắc cây ở trên, picker bằng chuột giả lập).

### Giao diện (feature_ui_polish)
- [x] **Action** là combo chung bên ngoài group box, cùng chỗ với Name và Category (Control và Command đã đổi nhãn Type thành Action; Mouse vẫn có "Type" là nút chuột).
- [x] 7 nút Add, Update, Delete, In, Out, Up, Down thành nút icon (Segoe MDL2 Assets nếu có, không thì ký hiệu thường) kèm tooltip.
- [x] **Action = Case** chỉ có trong danh sách khi đang chọn một dòng thuộc Condition (Begin, case, else, End hoặc action bên trong). Add/Update một Case bị từ chối nếu Condition đã có case cùng biểu thức (bỏ qua khoảng trắng, phân biệt hoa thường), vì case thứ hai không bao giờ chạy tới. "First case" đổi tên thành "Condition".
- [x] **Xác nhận khi mất dữ liệu:** New hỏi khi danh sách chưa lưu (không có file mà có dữ liệu) hoặc file đang mở đã bị sửa; file mở/lưu xong chưa sửa thì không hỏi. Cùng quy tắc áp dụng cho Open (hỏi sau khi chọn file) và khi đóng chương trình. "Đã sửa" so sánh JSON hiện tại với JSON lúc mở/lưu/new.
- [x] **Icon ?** cạnh các ô có giải thích: biểu thức (Variable, Position của Mouse, Text của Keyboard, Condition của case), Title (regex), Count (0 = forever), Anchor, Range anchor, Picture anchor, Width/Height (0 = tìm hết), ID, Accuracy, Wait up to, Retry, Time/Delay, Value type, Position/Range. Bấm vào mở cửa sổ giải thích (cú pháp biểu thức, toán tử, ngày, Position, thuộc tính, true/false/now/today, lỗi...).
- [x] **Đơn vị** (px, %, notches) nằm sau ô nhập thay vì trong nhãn.
- [x] Condition đã tạo: ô "Condition" chứa case đầu tiên; Update ghi lại (kiểm cú pháp và trùng case). Đóng chương trình khi script đang chạy: hỏi trước, trả lời No thì script vẫn chạy. Kích thước nút icon và "?" tính theo font nên vừa ở 125%/150% (chưa thử trên máy scale thật; harness chạy ở 96 DPI). Quy tắc trùng case giữ khoảng trắng trong chuỗi trong dấu nháy.
- Lưu ý kiểm thử: lần chạy harness cuối (sau khi sửa trình tự test) bị Application Control chặn exe mới, nên chưa chạy lại được; lần chạy ngay trước đó chỉ còn 2 lỗi do trình tự test (không phải lỗi sản phẩm).
- Test (harness): vị trí/ẩn hiện của Action, 7 nút icon, ô ? và đơn vị có mặt, bấm ? mở đúng nội dung, Case xuất hiện/biến mất theo vùng chọn (kể cả dòng End), từ chối case trùng khi Add và Update, New/đóng cửa sổ hỏi hay không hỏi đúng các tình huống.

### Chỉnh thêm (cùng nhánh feature_ui_polish)
- [x] 7 nút icon chuyển lên thanh công cụ, góc phải, thứ tự Add, Update, Delete, In, Out, Up, Down. Add luôn bật; các nút còn lại chỉ bật khi chọn ít nhất một action (dòng End không tính); tất cả tắt khi script đang chạy.
- [x] Giải thích ? của Time và Delay tách riêng (Mouse và Keyboard mỗi loại hai nội dung).
- [x] (Đã thay thế: Image giờ chỉ có một Anchor, không còn Picture anchor.)
- [x] **Capture picture:** trước khi hiện lớp phủ, chương trình chụp một ảnh màn hình (đã dời con trỏ ra góc để không hiện hover) rồi cho người dùng chọn trên chính ảnh đó (hiển thị mờ, vùng chọn sáng); ảnh lấy cắt từ ảnh chụp sẵn đó. Vì vậy màn hình thay đổi trong lúc đang chọn không ảnh hưởng ảnh mẫu. Test: đổi nội dung cửa sổ ngay giữa lúc kéo, ảnh nhận được vẫn đúng từng pixel với ảnh trước khi kéo.
- [x] **Mouse Move:** ô Type ẩn và không lưu giá trị Button vào file.
- [x] Bấm nút icon trên thanh công cụ chuyển focus khỏi ô đang sửa (ToolStrip không tự lấy focus): số vừa gõ được ghi nhận ngay (test: gõ 77 rồi bấm Add ra X = 77) và ô Key nhả phím F6 toàn cục. Vẽ lại lớp phủ đóng băng ở 1920x1080 mất ~11 ms mỗi lần. Khi bấm Capture, con trỏ nhảy xuống góc dưới phải khoảng 0,25 s (để không hiện hover); Pick range vẫn dùng lớp phủ trong suốt.

### Bố cục chi tiết (cùng nhánh)
- [x] Nút icon nằm cùng hàng với menu, canh trái cùng mép với phần Action detail (một ToolStrip riêng, rộng bằng panel chi tiết); icon đen đậm cỡ lớn hơn khi bật, xám nhạt khi tắt.
- [x] Tất cả nút chọn (program, position, range) đều ghi là **Pick** (tooltip nói rõ chọn gì; tôi hiểu "Pic" trong yêu cầu là "Pick"); nhãn hàng ảnh vẫn là "Picture".
- [x] Panel Action detail rộng 540 px. **Position** là một hàng hai ô (X, Y), **Size** là một hàng hai ô (Width, Height); nút Pick nằm bên phải ô Process, bên phải Position và bên phải Size; Browse bên phải Path.
- [x] Image: Capture và Pick (trước là Capture picture / Load file) nằm bên phải ảnh xem trước; hàng Accuracy là: ô số, %, nút Check (trước là Find now), rồi dấu ?.
- [x] Các ô số X, Y, Width, Height dùng `NumberBox` (ô chữ chỉ nhận số, có chữ mờ X / Y / Width / Height khi để trống; trống tính là 0; chỉ ô cho phép số âm mới nhận dấu trừ).

### Chỉnh theo yêu cầu người dùng (nhánh feature_user_tweaks)
- [x] Rà soát các sửa tay của người dùng (placeholder cho ô chữ, bỏ/đổi một số nhãn): `TextBox.Placeholder` không tồn tại nên không biên dịch được; đã thêm `HintTextBox` (ô chữ có gợi ý mờ: chữ một dòng dùng cue banner của hệ thống, nhiều dòng tự vẽ) và `NumberBox` kế thừa nó. Sửa lỗi chính tả "Póition". Phần còn lại giữ nguyên.
- [x] Nút Update chỉ bật khi chọn đúng một dòng.
- [x] Khoảng cách: Mouse (sau Type, sau Delay), Keyboard (trước Time), Wait (giữa Process và Wait), Image (sau ID; sau Size), Variable (sau Value type), và giữa Name, Category, Action, group box.
- [x] Panel Action detail thu còn 460 px (đo nội dung rộng nhất mọi trạng thái: 418 px + lề và thanh cuộn).
- [x] Wait: Retry có đơn vị "Times". Loop: kiểu `Count` đổi thành `Times` (nhãn, combobox, và tên lưu trong file; file cũ ghi "Count" vẫn đọc thành Times).

### Căn hàng và bố cục (nhánh feature_user_tweaks)
- [x] Mọi ô nhập rộng đúng 200 px (ô chữ, combobox, số, ngày giờ, TimeBox: Duration giờ bằng Type và Times, Wait bằng Retry; cặp X, Y và Width, Height chia nhau 200 px nên mép phải thẳng với Process, Picture, Accuracy...). Cột thứ hai cố định cho đơn vị (px, %, Times, notches) và các nút Pick / Check / Capture; cột thứ ba cố định cho dấu ?, nên các dấu ? thẳng hàng dọc. Name, Category, Action bắt đầu cùng x với ô trong group box. Panel Action detail 510 px. Test kiểm tra mép phải và cột ? của Mouse, Wait, Loop (3 kiểu), Image, Variable.
- [x] Các nút (Pick, Check, Capture, Pick file, Browse) cùng bắt đầu ở một x vì có cột đơn vị cố định (px, %, Times, notches) đứng trước cột nút; panel 510 px (nội dung rộng nhất 476 + lề + thanh cuộn).
- [x] Image và Wait/Mouse nhất quán: dòng Title không có nhãn, ô Process và Title có chữ mờ ("Process Name", "Process Title (Regex)").

### Record và chỉnh bố cục (nhánh feature_user_tweaks)
- [x] **Record** (nút cạnh Start/Stop, phím F7 để bắt đầu / dừng): ghi bằng hook bàn phím và chuột cấp thấp (thao tác vẫn đến chương trình bình thường). Bấm và nhả chuột tại cùng chỗ = Click; nhả ở chỗ khác = MouseDown + MouseUp (kéo, MouseUp có Time bằng thời gian kéo); các nấc lăn chuột liền nhau tại một chỗ = một Scroll với số nấc; phím = Key press (giữ bao lâu thì Time bấy lâu); phím bấm khi đang giữ Ctrl/Alt/Shift/Win = Combo key; phím bổ trợ bấm riêng = Key press của nó. Khoảng nghỉ đến action sau thành Delay (trừ 100 ms thời gian rê chuột tới điểm). Tọa độ là tọa độ màn hình (không có Process). Không ghi: di chuyển chuột đơn thuần, thao tác trên chính cửa sổ chương trình, phím F6 và F7, phím không có tên. Kết quả chèn như Add: vào trong Loop / Wait / Image / case / else đang chọn, sau action đang chọn (sau cả Condition nếu chọn dòng Condition), hoặc cuối danh sách nếu không chọn gì. Đã test qua hook thật bằng thao tác giả lập (click, kéo, lăn 3 nấc, A, Ctrl+C, F6 bị bỏ qua, F7 dừng) và bộ chuyển đổi bằng sự kiện tổng hợp.
- [x] Khi đang Start hoặc Record: mọi nút và ô bị khóa (New, Open, Save, Save As, các icon, danh sách, phần chi tiết); chỉ còn Stop của việc đang chạy (Start hoặc Record).
- [x] Start/Stop và Record nằm giữa thanh menu (khoảng trống phía trước tự co giãn theo bề rộng).
- [x] Đơn vị chuyển vào nhãn: Position (px), Range (px), Size (px), Accuracy (%), Retry (times), Scroll (notches). Không còn cột đơn vị; nút Pick/Check/Capture/Browse và dấu ? dùng chung một cột ngay sau ô: hàng không có nút thì dấu ? đứng đúng chỗ nút; hàng có nút thì ? nằm sau nút.
- [x] Image: hàng vùng tìm đổi tên "Range (px)", hàng kích thước không còn nhãn "Size"; ảnh xem trước không mất viền phải; nút Capture không bị cắt. Panel 475 px.

### Checklist test giao diện (feature_ui_polish)
1. Bấm các dấu ? (Count, Title, Anchor, Expression, ID, Accuracy...): cửa sổ giải thích hiện đúng nội dung, đọc được, đóng bằng Close/Esc.
2. New khi danh sách có dữ liệu chưa lưu: hỏi xác nhận; No thì giữ nguyên, Yes thì xóa. Mở/lưu file rồi New khi chưa sửa: không hỏi; sửa rồi New: hỏi và nêu tên file. Open và đóng chương trình cũng hỏi như vậy (ngoài yêu cầu ban đầu, có thể bỏ).
3. Action = Case chỉ có khi chọn một dòng thuộc Condition; thử tạo hai case giống nhau (kể cả khác khoảng trắng): bị từ chối.
4. Chọn Condition: ô Condition hiện case đầu; sửa rồi Update thì case đầu đổi.
5. Nút icon rõ ràng, hover hiện tên; xem ở 125% và 150%: glyph và dấu ? không bị cắt.
6. Action nằm ngoài group box, Control/Command ghi Action; đơn vị px, %, notches nằm sau ô nhập.
7. Nút icon trên thanh công cụ nằm thẳng mép trái với phần Action detail; chưa chọn gì thì chỉ Add đậm, chọn một dòng thì các nút khác đậm; xem ở 125%/150% không bị cắt.
7b. Position/Size một hàng với ô mờ gợi ý; nút Pick đúng chỗ bên phải (hover xem tooltip); Image: Capture, Pick, Check.
8. Capture picture: bấm Capture lúc màn hình có thứ đang thay đổi (hiệu ứng, hover, popup đóng): ảnh trong Picture đúng với những gì thấy lúc khoanh.
9. Mouse, Action = Move: không có ô Type; lưu file thì action Move không có "Button".

## Việc tiếp theo
- [ ] Người dùng test giao diện (checklist dưới), báo lỗi nếu có.
- [ ] PR vào `preview` khi người dùng yêu cầu (một PR từ `feature_variables` gồm cả chuỗi).
- [ ] Record; License (M5).

### Checklist test giao diện (Variable, Condition, biểu thức)
1. Control > Type: Loop, Wait, Image, Variable, Condition, Case (không có Else).
2. Variable: nhập ID `abc`, kiểu Number, giá trị 5, Add. Thử ID sai (`1a`, có dấu cách, `true`): bị từ chối. Tick "Use an expression", nhập `{abc} +`: báo lỗi kèm vị trí; `{abc} + 1`: thêm được. Thử các kiểu Position/Range (có Pick position / Pick range, chọn Process + Anchor), Boolean, Date, DateTime.
3. Condition: Category Control, Type Condition, nhập ô "First case" (bắt buộc, ví dụ `{abc} > 3`), Add: xuất hiện 4 dòng (Begin, case đầu, else, End). Chọn Condition, Type = Case, nhập `{abc} > 2`, Add: case mới nằm trước else (sau case đầu). Update trên Condition chỉ đổi tên. Chọn một case rồi Add action Mouse: nằm trong case (thụt lề). Thử xóa else / xóa case cuối / Up-Down case qua else / In-Out case: bị từ chối với thông báo. Out một action từ trong case: nằm ngay sau dòng End.
4. Chạy: Variable abc = 5, Condition với `{abc} > 3` và `{abc} > 2`: chỉ case đầu chạy (thêm action nhìn thấy được, ví dụ Text). Đổi abc = 2 thì chạy else.
5. Loop 5 lần chứa Variable `{n} + 1` rồi Condition: số lần mỗi nhánh đúng.
6. Image: đặt ID `img` và Anchor (dùng chung cho vùng tìm và Position trả về). Thêm Mouse, Position from = Variable, Expression `{img.Position} + {10,20}`, cùng Process và Anchor với Image: chuột đến đúng điểm; dời cửa sổ rồi chạy lại vẫn trúng. Thử `{img.Position.X + 1}, {img.Position.Y + 1}`.
7. Condition dùng `{img.Found}`: ảnh có/không có trên màn hình cho nhánh đúng.
8. Keyboard Text, Text from = Variable: `"Total: " + {sum}` gõ đúng.
9. Save As chỉ hiện sau khi Open hoặc Save lần đầu.
10. Save, Open lại: biến, condition, ID/anchor của Image giữ nguyên.

### Ghi chú cho milestone sau
- Trường mới có giá trị mặc định khác 0 phải đặt trong `[OnDeserializing] SetDefaults` của lớp tương ứng và không dùng `EmitDefaultValue=false` cho chúng. Các sub-object (Mouse/Keyboard/Command/Loop/Wait/Image/Variable/Case) là tham chiếu nên dùng `EmitDefaultValue=false` an toàn.
- `ImageLocator.Locate` là điểm duy nhất biến cài đặt Image thành phép tìm trên màn hình (runner, Find now, Pick position đều dùng).
- Biến là toàn cục trong một lần chạy, xóa sạch ở đầu mỗi lần Start.
- Khi Process khớp nhiều cửa sổ, runner lấy cửa sổ khớp đầu tiên theo z-order (ưu tiên không minimize); cần điền Title (regex) để chỉ rõ.
- Đổi Anchor sau khi pick sẽ giữ nguyên số X/Y nhưng đổi nghĩa; phải pick lại.
- Multi-select giữ bằng ListView thụt lề (TreeView của WinForms không chọn nhiều được). Chưa có thu gọn/mở rộng nhánh.
- Delay ngắn (<20 ms) và mỗi vòng lặp không delay dùng chờ có nhường CPU (không dùng timer) vì Windows 11 bỏ qua `timeBeginPeriod` khi app thu nhỏ.
- Phím Win không bắt được trong ô Key. Gõ Text dùng Unicode packet; chưa thử khi Unikey/EVKey đang bật.
- Chụp màn hình bằng BitBlt có thể ra màu đen với nội dung DirectX/toàn màn hình độc quyền. Accuracy là ngưỡng giống nhau, không xử lý ảnh mẫu bị che một phần.
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

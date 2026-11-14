# Roadmap – kế hoạch tính năng tiếp theo

Tài liệu này ghi các tính năng đã được yêu cầu nhưng chưa làm, cách thiết kế dự kiến, thứ tự làm và những điểm cần người dùng quyết định.
Tình trạng hiện tại xem `docs/STATUS.md`, yêu cầu gốc và quyết định kỹ thuật xem `docs/PROJECT.md`.
Mỗi giai đoạn là một nhánh `feature_xxx` tách từ `preview`, xong thì PR vào `preview` (quy ước git trong `CLAUDE.md`).

Cập nhật lần cuối: 2026-11-15. Trạng thái: **P0 (bộ test trong repo) đã xong phần chính; P1 (R1 và R14: Try/Catch/Break/Continue/Return và Section) đã xong**; các giai đoạn còn lại chưa bắt đầu. P0 chưa có: test cho Web (cần trình duyệt và extension), RecordConverter, overlay/hook/phím nóng và Image trên cửa sổ thật.

## 1. Danh sách yêu cầu

Mã R1, R2... dùng để tham chiếu trong các mục sau.

### R1. Try / Catch / Break / Continue / Return (Control) – đã làm (P1)
- **Try** là action chứa con, kèm một nhánh **Catch** ở cuối (cùng kiểu cấu trúc với Condition và Else: nhánh Catch nằm trong Try, không Add trực tiếp vào Try). Lỗi của một action trong Try thì phần còn lại của Try bị bỏ qua và nhánh Catch chạy. Catch có ô ID tùy chọn: biến nhận thông báo lỗi dạng chuỗi.
- **Break** thoát Loop gần nhất, **Continue** sang vòng kế tiếp của Loop gần nhất, **Return** dừng kịch bản hiện tại (kịch bản chính thì kết thúc lần chạy, kịch bản con của Auto Script thì trở về chỗ gọi). Editor từ chối Break/Continue nằm ngoài Loop.
- Stop (phím dừng), hủy lần chạy, lệnh tắt máy và các tín hiệu Break/Continue/Return **không** bị Try bắt. **Mọi lỗi khác** của action đều vào Catch, không chỉ `InvalidOperationException`: `WindowLocator` ném `ArgumentException`, còn COM, hết thời gian và lỗi Win32 cũng có thể thoát ra ngoài; nếu chỉ bắt một loại lỗi thì Try mất tác dụng.
- Runner hiện đi đệ quy và dùng ngoại lệ cho hủy/tắt máy; luồng Break/Continue/Return nên là giá trị trả về của `ExecuteList` (không dùng ngoại lệ) để không lẫn với lỗi.
- Ảnh hưởng: quy tắc cây (Add/In/Out/Up/Down với Try và Catch giống Condition), cột Time (Try lấy nhánh Try, bỏ qua Catch), Start Selected, Details, Help.
- **Tương thích:** kiểu Control lạ mà bản cũ không biết bị đọc thành **Loop** với số lần mặc định là 1 (không phải vô hạn). Hậu quả với bản cũ: Section chạy các con một lần (vô tình đúng); Try chạy các con một lần rồi **nhánh Catch cũng chạy vô điều kiện** vì nó cũng thành một Loop; Break/Continue/Return không làm gì. Ghi vào mục cảnh báo bản cũ.

### R14. Section (Control) – đã làm (P1)
- Action chứa con, không tự làm gì, có Name và State. Chạy = chạy các con một lần. State OFF thì bỏ qua cả nhóm. Cột Time của Section = tổng Time + Delay của các con đang bật, tính như Loop một lượt (`ActionTiming`).
- Break/Continue/Return đi xuyên qua Section (Section không phải vòng lặp). Có thể thu gọn/mở rộng như các action cha khác.
- Cùng giai đoạn với R1 vì cùng đụng quy tắc cây và runner.

### R2. Setting "Log" (mặc định No)
- Hộp Settings có ô **Log**; Yes thì mỗi lần chạy (Start hoặc Start Selected) ghi **một file log riêng**. Lưu ở `Setting.ini` mục `[Options]` như các tùy chọn khác.
- Đề xuất: thư mục `Logs` cạnh exe, tên `yyyyMMdd_HHmmss_<tên kịch bản>.log`, mỗi dòng có giờ; ghi bắt đầu/kết thúc lần chạy, mỗi action (đường dẫn trong cây + Name + loại) bắt đầu/xong, lỗi và nhánh Catch, lý do kết thúc (xong, bị Stop, lỗi, Return).
- **Bảo mật:** log **không bao giờ** ghi giá trị trường, giá trị biến, nội dung gõ hay phản hồi API. Chỉ ghi tên action và thông báo lỗi; thông báo lỗi cũng phải đi qua bước che các giá trị Password đã biết. Không ghi header/token của API và tài khoản Email.
- Chưa có tự xóa log cũ (xem mục quyết định).

### R3. Setting "Message" (mặc định No)
- Hộp Settings có ô **Message**; Yes thì khi lần chạy kết thúc (xong hoặc lỗi) hiện thông báo cho người dùng. Chạy lâu và thu nhỏ ở khay hệ thống thì thông báo quan trọng nhất.
- Đề xuất: bong bóng thông báo của icon khay (`NotifyIcon`, không cần thư viện thêm). Nội dung ngắn: xong / bị dừng / lỗi kèm câu lỗi và tên action.

### R4. Setting "Random Time" (mặc định No)
- Yes thì lúc chạy thật, **mỗi lần** thực hiện một action, các giá trị **Time** và **Delay** của nó được cộng thêm một khoảng ngẫu nhiên **10–20%** của chính giá trị đó (chỉ cộng thêm, không bớt).
- Chỉ áp dụng cho Time (thời gian di chuột, giữ phím, gõ) và Delay (chờ sau action). **Không** áp dụng cho Timeout, Retry, Wait, thời gian đếm ngược Shutdown, thời lượng Loop kiểu Duration/Until.
- Hiển thị (cột Time, tổng của Loop/Section) vẫn là giá trị đã cài, không phải giá trị ngẫu nhiên.
- Làm một chỗ duy nhất trong runner (một hàm trả lại giá trị đã cộng) để mọi loại action dùng chung.

### R5. Category Program (tách khỏi Utilities)
- Category mới **Program** có các action: **Open**, **Close** (đã có, chuyển từ Utilities), rồi thêm thao tác cửa sổ: **Move**, **Resize**, **Minimize**, **Maximize**, **Restore**, **Activate** (đưa lên trước), có thể thêm **Close window** (đóng một cửa sổ theo tiêu đề, khác Close cả chương trình).
- Chọn chương trình/cửa sổ bằng cùng bộ **Match** của Close (Title regex, Name, Path, Id), có Pick giống Mouse nếu làm được.
- Utilities còn: File/Folder, System (Active, Shutdown), và sẽ thêm API, Auto Script (R8, R12).
- **Chuyển đổi:** khi mở file, action Utilities có kiểu Open/Close được đọc thành Program; lưu lại thì ghi theo category mới.
- **Tương thích:** bản cũ không biết category Program, đọc thành **Mouse** mặc định (chạy sẽ click bậy). Ghi vào cảnh báo.

### R6. Variable kiểu Clipboard
- Kiểu mới của action Variable. Khi action chạy, biến được gán bằng **chuỗi** đang có trong clipboard tại thời điểm đó; sau đó clipboard đổi thì biến **không** đổi theo.
- Clipboard rỗng hoặc không phải chữ (hình, file) thì giá trị là chuỗi rỗng (đề xuất; hoặc lỗi nếu bật tùy chọn).
- Đọc clipboard phải chạy trên luồng STA; runner chạy trên luồng riêng nên cần cầu nối (marshal sang luồng giao diện hoặc luồng STA riêng).

### R15. Variable kiểu Password
- Giá trị **không bao giờ lưu trong file JSON**. Lúc chạy, khi gặp action Variable kiểu Password thì hiện hộp nhập (ô che ký tự); giá trị chỉ nằm trong bộ nhớ của lần chạy đó.
- Đề xuất: hỏi **một lần cho mỗi ID trong mỗi lần chạy** (không hỏi lại ở mỗi vòng Loop); lần chạy sau lại hỏi.
- Chỉ dùng được qua biểu thức `{pw}` (ví dụ Keyboard Text, Web Input, API header, Email). Không hiện trong cột Details, hộp nhập biến của Start Selected (R7) thì cũng che và **không nhớ**, và không bao giờ vào log (R2).
- Hủy hộp nhập thì dừng lần chạy.
- Việc "không lưu" phải được bảo đảm **ở chỗ dựng node và ghi file**, không chỉ ẩn ô nhập: đổi một biến String đang có giá trị sang Password thì giá trị phải bị xóa, không để sót trong JSON (cùng loại lỗi trường ẩn còn dư giá trị đã gặp ở Excel).
- Giới hạn cần ghi rõ cho người dùng: chạy tự động không người ngồi (hẹn giờ/dòng lệnh, nếu sau này làm) không hỏi mật khẩu được.

### R7. Start Selected nhớ giá trị biến của lần trước
- Hộp nhập biến của Start Selected nhớ giá trị đã nhập **ở lần Start Selected ngay trước đó**. Biến cùng **tên và kiểu** với một biến của lần trước thì tự điền giá trị cũ; khác kiểu hoặc không có ở lần trước thì để trống như hiện nay.
- Chỉ nhớ đúng một lần trước đó (lần sau thay hẳn): ví dụ lần 1 có `a` String, `b` Number, `c` Number; lần 2 có `a` String và `b` String → `a` được điền, `b` không (khác kiểu); lần 3 có `c` Number → không điền vì `c` không có ở lần 2.
- Chỉ lưu trong bộ nhớ của phiên làm việc (không ghi ra file). Không bao giờ nhớ biến Password.
- Giá trị nhớ **thắng** giá trị mặc định mà hộp lấy từ action Variable cùng tên trong kịch bản. Bấm Hủy hộp nhập thì lần đó không thay bộ nhớ.

### R10. Image trở thành kiểu của Variable
- Chuyển action **Control/Image** thành kiểu **Image** của action Variable: Variable Image tìm ảnh (Range, Process, Anchor, ảnh mẫu, Accuracy) và lưu kết quả vào ID (`Found`, `Position`, `Size`) như hiện nay. Phần "nếu thấy thì chạy các con" chuyển sang **Condition** dùng `{id.Found}`.
- **Chuyển đổi tự động khi mở file cũ:** mỗi action Image cũ có con thành `Variable (Image)` + `Condition` có một Case `{id.Found}` chứa các con cũ; Image không có con thành Variable (Image). Giữ khả năng đọc action Image cũ mãi. Cần test bằng các file kịch bản thật đã lưu.
- Image cũ có ID để trống được: khi chuyển phải tự sinh một ID duy nhất (không trùng biến nào trong kịch bản) cho Variable và Condition. Name và State của action cũ được chép cho cả hai node mới.
- Sau khi file đã chuyển và lưu lại thì bản cũ **không đọc được các Image đó** nữa (không biết kiểu Variable Image).
- Sau khi chuyển, Control không còn Image trong combo; cột Time, Start Selected, `SelectionPlan` (biến do Image định nghĩa) phải cập nhật.
- Đây là thay đổi **phá vỡ** lớn nhất trong danh sách: làm sau khi có bộ test.

### R11. Variable kiểu OCR
- Kiểu OCR: chọn một vùng (cùng Range, Process, Anchor như Image), nhận diện chữ trong vùng, biến nhận chuỗi (có thể có thêm thuộc tính sau này).
- Phụ thuộc vào công cụ OCR, xem mục quyết định. Đề xuất dùng OCR có sẵn của Windows (không cài thêm, nhưng cần gói ngôn ngữ OCR của Windows cho tiếng Việt) và làm thử nhỏ trước khi chốt.

### R8. Utilities: API
- Nhóm **API** trong Utilities với các kiểu: GET, POST, PUT, PATCH, DELETE, HEAD (và kiểu "FETCH" theo cách hiểu của người dùng: xem mục quyết định).
- Trường: URL, query, headers, body (chữ, JSON, form), xác thực (không, Bearer, Basic; token/mật khẩu qua biểu thức, ví dụ biến Password), timeout, theo dõi chuyển hướng, kiểm tra chứng chỉ HTTPS (luôn bật).
- **ID** nhận kết quả: `Status` (số), `Ok` (Boolean), `Body` (chuỗi), `Headers`, và truy cập JSON trong biểu thức kiểu `{r.Json.data[0].name}`. Cần mở rộng bộ biểu thức với giá trị JSON (thuộc tính và chỉ số).
- Có tùy chọn lưu phản hồi ra file (tải xuống).
- Dùng `HttpClient` (có sẵn trong .NET Framework 4.8); log và thông báo lỗi không in token/body.

### R9. Category Email
- Category mới **Email** với: Send, Read, và có thể Reply, Move, Mark read, Save attachments, Delete.
- Read trả về dữ liệu vào ID (danh sách thư dạng Table và/hoặc một thư với thuộc tính From, Subject, Body, Date, Attachments).
- Cách kết nối chưa chốt, xem mục quyết định (Outlook COM hay SMTP/IMAP).
- Tài khoản và mật khẩu không lưu trong JSON: dùng biến Password (R15) hoặc thông tin đăng nhập có sẵn của Outlook.

### R12. Utilities: Auto Script
- Nhóm **Auto Script** trong Utilities: chạy một kịch bản con từ file JSON đã lưu để dùng lại.
- Trường: đường dẫn file (tương đối so với thư mục kịch bản cha được), danh sách **tham số vào** (tên = biểu thức), danh sách **biến trả về** (sao chép về kịch bản cha khi xong), cô lập biến (kịch bản con có kho biến riêng, chỉ nhận tham số vào).
- Phiên Excel và Web (workbook, sheet, tab đang dùng) của kịch bản con: **dùng chung** với kịch bản cha (để kịch bản con làm việc trên workbook cha vừa mở); biến thì cô lập. Lỗi trong kịch bản con đi lên cha như lỗi của chính action Auto Script, nên Try của cha bắt được. Kiểm tra `Version` của file con và báo rõ khi không đọc được.
- Return (R1) trong kịch bản con trở về chỗ gọi. Giới hạn độ sâu gọi lồng (đề xuất 16) và phát hiện A gọi chính A.
- Cột Time của action: nếu đọc được file con thì tính tổng, không thì hiện "?".
- **Bảo mật:** chạy file kịch bản của người khác có thể xóa file, tắt máy, đóng chương trình: ghi cảnh báo giống Open/Shutdown trong Help (`?`).
- Mật khẩu: kịch bản con cần Password thì hỏi như bình thường (một lần mỗi ID mỗi lần chạy).

### R13. Copy/paste action và kéo thả trong danh sách
- **Ctrl+C / Ctrl+X / Ctrl+V** trong danh sách action: sao chép/cắt các action đang chọn (kèm toàn bộ con). Dữ liệu đặt vào clipboard với một định dạng riêng (JSON của các node), nên dán được sang kịch bản khác hoặc lần mở khác. Dán chèn như Add: vào container đang chọn hoặc sau dòng đang chọn; tuân theo quy tắc cây (không dán vào Condition trực tiếp, v.v.). ID biến và Name giữ nguyên.
- **Kéo thả** để sắp xếp: kéo nhiều dòng, thả lên/xuống/vào trong container, có đường chỉ vị trí thả; từ chối thả vào chính con cháu của nó hoặc vào chỗ vi phạm quy tắc Condition/Try.
- **Đề xuất thêm: Undo/Redo** cho danh sách action. Hiện chưa có hoàn tác, mà kéo thả và dán dễ làm hỏng kịch bản; nên làm cùng giai đoạn này (một ngăn xếp snapshot JSON của `AutomationScript` là đủ).

## 2. Thứ tự làm và phụ thuộc

| Giai đoạn | Nội dung | Vì sao đặt ở đây | Cỡ |
|---|---|---|---|
| P0 (**xong phần chính**) | Đưa bộ test vào repo (`tests/`): biểu thức, runner, vòng nạp/lưu editor, thư mục tạm, Excel ẩn riêng | Mọi giai đoạn sau đều cần test; trước đây test nằm ở thư mục tạm của phiên và mất đi | M |
| P1 (**xong**) | R1 Try/Catch/Break/Continue/Return + R14 Section | Nền cho cây và runner; Return cần cho Auto Script | L |
| P2 | R2 Log, R3 Message, R4 Random Time (hộp Settings) | Log nên ghi được Try/Catch từ đầu; ba ô cùng một hộp | M |
| P3 | R6 Clipboard, R15 Password, R7 nhớ giá trị Start Selected | Nhỏ, độc lập, Password cần có trước API/Email/Auto Script | M |
| P4 | R13 copy/paste, kéo thả, (Undo/Redo) | Sau P1 để quy tắc cây (Try, Section) đã chốt | L |
| P5 | R5 Category Program + thao tác cửa sổ | Tách category kéo theo chuyển đổi file | M |
| P6 | R10 Image → Variable, R11 OCR | Thay đổi phá vỡ lớn nhất; OCR cần thử công cụ trước | L |
| P7 | R12 Auto Script | Cần Return (P1), biến/Password (P3) | M |
| P8 | R8 API | Cần giá trị JSON trong biểu thức | L |
| P9 | R9 Email | Phụ thuộc quyết định kết nối | L |

Có thể đổi thứ tự P4–P9 theo nhu cầu; P1 → P2 → P3 nên giữ vì các giai đoạn sau dựa vào chúng.

## 3. Nguyên tắc chung cho mọi giai đoạn
- **Định dạng file:** chỉ thêm, không đổi tên hay bỏ; kiểu mới thêm vào cuối enum; trường mới có giá trị mặc định đúng và `EmitDefaultValue = false`; trường chuỗi mới đọc về `null` ở file cũ thì dùng getter trả `""` như `UseWorkbook`. File cũ luôn mở được.
- **Bản cũ đọc file mới:** kiểu lạ bị đọc thành mặc định. Các mặc định hiện có: Category → **Mouse** (click bậy), Control → **Loop** một lần, Utilities → **Program Open** (mở đường dẫn, kể cả của Delete), Excel → **Workbook** (Path trống: tạo workbook mới), Web → **Tab New**, Variable → **Number**, Loop → Times. Mỗi giai đoạn ghi rõ trong `STATUS.md` mục "bản cũ" và, nếu có thể, tránh để kiểu mới trùng với một mặc định nguy hiểm.
- **Bảo mật:** không lưu bí mật trong JSON; không ghi bí mật vào log, Details hay thông báo; mọi action mới có thể xóa/gửi/chạy gì đó đều có cảnh báo trong Help như Open và Shutdown.
- **Test:** mỗi giai đoạn có test cho logic (runner, mô hình), vòng nạp/lưu của editor cho mọi Kind mới và kiểm từng trường, và test thật với Excel/Word/... ở thư mục và tiến trình riêng của test (kiểm không để sót tiến trình).
- **Editor:** thêm vào bảng ánh xạ nhóm (`ActionGroups`), kiểm `Covers()`, Help, Details, cột Time, `SelectionPlan` (biến do action định nghĩa/đọc), kiểm tra hợp lệ trong `Validate`.
- **Kết thúc mỗi giai đoạn:** build `Release` bằng `build-release.bat` (giữ file của người dùng), cập nhật `STATUS.md` và `PROJECT.md`, PR vào `preview`.

## 4. Quyết định cần người dùng xác nhận (kèm đề xuất)

| # | Câu hỏi | Đề xuất |
|---|---|---|
| 1 | Log nằm ở đâu, có tự xóa log cũ không? | `Logs` cạnh exe; chưa tự xóa (thêm "giữ N file gần nhất" sau nếu cần) |
| 2 | Message dùng cơ chế nào? | Bong bóng của icon khay, nhưng chương trình ẩn icon khi lần chạy kết thúc (`ShowFromTray`) nên bong bóng có thể không kịp thấy: nếu vậy dùng hộp thoại nhỏ hoặc thông báo Windows; lỗi thì kèm tên action và câu lỗi |
| 3 | Catch có ID chứa thông báo lỗi và có nhánh Finally không? | Có ID tùy chọn; chưa làm Finally |
| 4 | Mở file cũ có action Image: tự chuyển thành Variable Image + Condition? | Có, giữ đọc được action Image cũ |
| 5 | OCR dùng công cụ nào? | OCR có sẵn của Windows (cần gói ngôn ngữ), làm thử nhỏ trước khi chốt; Tesseract là phương án dự phòng (thêm hàng chục MB) |
| 6 | Email kết nối bằng gì? Bạn đang dùng Outlook cổ điển (classic) hay Outlook mới? | Outlook COM chỉ chạy với **Outlook cổ điển** (Outlook mới của Windows không có COM theo hiểu biết của tôi, cần kiểm lại); nếu bạn dùng Outlook mới hoặc Gmail thì SMTP/IMAP (mật khẩu ứng dụng/OAuth) là cách duy nhất. Đề xuất: biết bạn dùng gì rồi mới chốt |
| 7 | Kiểu "FETCH" của API nghĩa là gì? | Dùng GET/POST/PUT/PATCH/DELETE/HEAD và thêm "Download" (lưu ra file); nếu bạn muốn khác thì nói |
| 8 | Password hỏi một lần mỗi lần chạy hay mỗi lần action chạy? | Một lần cho mỗi ID mỗi lần chạy |
| 9 | Random Time áp dụng cho những trường nào? | Chỉ Time và Delay, cộng thêm 10–20%, không bớt |
| 10 | Làm P0 (bộ test trong repo) trước? | Có |
| 12 | Kịch bản con (Auto Script) dùng chung phiên Excel/Web với kịch bản cha? | Dùng chung (biến thì cô lập) |
| 13 | Category Program lưu thật thành category mới hay chỉ hiển thị riêng nhưng vẫn lưu dưới Utilities (Open/Close)? | Lưu thành category mới cho gọn; cách "chỉ hiển thị" giữ cho bản cũ vẫn đọc được Open/Close nhưng làm cấu trúc file rối. Cần bạn quyết |
| 11 | Có làm Undo/Redo cùng P4? | Có |

## 5. Ngoài danh sách (đã đề xuất trước đó, chưa nhận)
Hẹn giờ và chạy từ dòng lệnh, ký số, trình cài đặt, kích hoạt license online, Record cho Web và Excel, gỡ lỗi (bảng biến, chạy từng bước, breakpoint), tìm kiếm trong danh sách action, thư viện kịch bản mẫu, kiểm tra bản mới. Chưa lên kế hoạch.

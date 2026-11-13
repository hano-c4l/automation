# Automation Test – Project Overview

Tài liệu này ghi lại yêu cầu, quyết định kỹ thuật và kế hoạch. Tình trạng hiện tại xem `docs/STATUS.md`.
Quy ước chung (git, ngôn ngữ, subagent) xem `CLAUDE.md` ở thư mục gốc.

## 1. Mục tiêu

Chương trình automation/autoclick cho Windows, người dùng cuối không phải cài thêm gì.

### Chức năng cơ bản
- Chọn 1 hoặc nhiều điểm trên màn hình.
- Mỗi bước cấu hình được: chuột trái/phải, mouse-down, mouse-up, move.
- Thời gian thực hiện mỗi hành động (ví dụ Move 3s chậm hơn Move 1s).
- Thời gian chờ trước bước tiếp theo.
- Số lần lặp lại (hữu hạn hoặc vô hạn).

### Chức năng nâng cao
- Gửi 1 phím (ví dụ F2 để edit cell) hoặc 1 đoạn text (kể cả tiếng Việt).
- Nhận diện hình ảnh trong một vùng, kèm điều kiện: nếu thấy ảnh mẫu thì click vào đó hoặc click một điểm chỉ định; nếu không thấy thì làm nhánh khác.
- Lưu/mở kịch bản thành file để dùng lại.
- Tọa độ theo cửa sổ chương trình đích (offset so với vùng client), để cửa sổ dời chỗ vẫn click đúng.
- License theo máy, có hoặc không có thời hạn, chống chia sẻ key.

## 2. Quyết định đã chốt

| Mục | Quyết định |
|---|---|
| Nền tảng | Chỉ Windows (10 1903+ và 11) |
| Ngôn ngữ / runtime | C# trên **.NET Framework 4.8** (có sẵn trong Windows, file vài MB) |
| Giao diện | WinForms |
| Nâng cấp sau | Có thể lên .NET 10 LTS; lõi target `netstandard2.0` để dùng lại, project dùng SDK-style |
| Khác DPI / resolution giữa các máy | Chấp nhận phải setup lại, không xử lý |
| Ký số | Chưa ký ở giai đoạn đầu; xem lại khi phát hành rộng |
| License | Offline, chữ ký số, ràng buộc Machine ID (xem mục 5) |

## 3. Kiến trúc dự kiến

```
AutomationTest.sln
  src/AutomationTest.Core     (netstandard2.0)  model kịch bản, JSON, engine chuột phím (P/Invoke), so khớp ảnh,
                                            biểu thức/biến, chuyển bản ghi thành action, runner,
                                            tọa độ theo cửa sổ; kiểm tra license (Licensing/)
  src/AutomationTest.App      (net48 WinForms)  UI cây action, overlay chọn điểm/vùng, phím nóng, hook ghi thao tác
  tools/AutomationTest.KeyGen (net48 console)   công cụ tạo license, KHÔNG đóng gói cùng bản phát hành
```

## 4. Điểm kỹ thuật bắt buộc

1. **DPI:** khai báo Per-Monitor V2 trong manifest. `SendInput` dùng tọa độ chuẩn hóa 0–65535; nhiều màn hình thì dùng `MOUSEEVENTF_VIRTUALDESK`.
2. **Gõ tiếng Việt:** `SendInput` với `KEYEVENTF_UNICODE`; dự phòng bằng clipboard + Ctrl+V. Unikey/EVKey đang bật có thể làm méo text.
3. **UIPI:** chỉ điều khiển được cửa sổ admin khi chương trình cũng chạy admin. Cần tùy chọn chạy admin.
4. **Move theo thời gian:** nội suy có easing trong thời gian đã đặt, dùng `Stopwatch` + `timeBeginPeriod(1)`; không dùng `Thread.Sleep` đơn thuần.
5. **Nhận diện ảnh:** so khớp mẫu (ưu tiên tự viết NCC bằng C# cho nhẹ; OpenCvSharp thêm ~50–60MB nên là phương án dự phòng) với ngưỡng giống nhau, chỉ tìm trong vùng đã định nghĩa.
6. **An toàn / phím nóng:** các phím Start, Start Selected và Record (mặc định F3, F4, F6, đổi được trong Settings) bằng `RegisterHotKey` khi rảnh; khi script chạy hoặc đang ghi thì dùng hook bàn phím cấp thấp (bỏ qua phím do chính chương trình gửi, không nuốt phím) để chỉ phím đã bắt đầu lần chạy (Start cho chạy cả script, Start Selected cho chạy phần chọn) mới dừng được; phím kia và Esc không dừng script (Esc chỉ hủy Pick) (phím Record dừng khi đang ghi) mà script gửi các phím đó cho chương trình khác không bị chặn. Tùy chọn dừng khẩn cấp khi đưa chuột vào góc màn hình: chưa làm.
7. **SmartScreen/antivirus:** exe chưa ký gọi `SendInput` có thể bị cảnh báo. Người dùng chọn "More info → Run anyway" hoặc thêm Defender exclusion. **Smart App Control** (Windows 11) thì chặn hẳn exe chưa ký ("did not meet the Enterprise signing level"): Defender exclusion không có tác dụng và chứng chỉ tự ký, kể cả đã import vào Root và Trusted Publisher của máy, cũng không được chấp nhận (đã thử). Cách xử lý: tắt Smart App Control trên máy đó (tắt là vĩnh viễn), hoặc ký bằng chứng chỉ từ CA tin cậy (ví dụ Trusted Signing) khi phát hành rộng. Máy Windows 10 hoặc máy đã tắt SAC không bị.

## 5. Mô hình kịch bản và file lưu

**Cơ chế (thiết kế lại ở phiên bản 2, thay danh sách bước phẳng):** kịch bản là **cây action**.
- UI: thanh công cụ (New/Open/Save/Save As, Start/Stop và Record ở giữa, các nút icon Add, Update, Delete, In, Out, Up, Down; sau này thêm nhập License); panel trái là danh sách action thụt lề theo cấp (chọn nhiều được); panel phải là cài đặt action.
- Category: Control, Utilities (trước đây là Command), Mouse, Keyboard. Mỗi action có thêm State (bật/tắt, lưu dạng `Disabled`). **Đã làm:** Mouse, Keyboard (KeyPress/KeyUp/KeyDown/ComboKey/Text; không có Target, gửi tới cửa sổ đang focus), Web (OpenBrowser, OpenUrl, WaitElement, Input, Click, Dialog, GetText, SwitchTab, Frame; xem STATUS), Utilities (Open, Active, Shutdown: Time chờ rồi tắt máy, tùy chọn Force), Control/Loop (Times/Duration/Until), Control/Wait, Control/Image (ZNCC, ảnh lưu trong file script; ID thành biến Found/Position/Size, một Anchor), Control/Variable, Control/Condition (Case/Else), biểu thức `{biến}` và Record (F7). Mọi action có Name (alias). Mouse chỉ lấy tọa độ từ nhập/picker hoặc biểu thức; Image không làm gốc tọa độ cho action con. **Chưa làm:** License.
- Mouse: Action (Click, MouseUp, MouseDown, Move, ScrollUp, ScrollDown; scroll bắt buộc dùng nút giữa; Move không có Type), Type (Left/Right/Middle), Time (thời gian di chuyển chuột tới vị trí; với Keyboard là thời gian giữ phím), Delay (chờ sau action), Process (tên process + regex tiêu đề), Anchor, vị trí X/Y. Process trống = tọa độ tuyệt đối trên desktop; có Process = offset so với vùng client của cửa sổ đó theo Anchor.
- Loop: chứa các action con (Add khi đang chọn Loop, hoặc In). Type: Times (0 = vô hạn), theo khoảng thời gian, hoặc tới một thời điểm. Thời gian/thời điểm chỉ kiểm tra giữa các vòng nên vòng cuối có thể vượt mốc tối đa bằng một lượt thân loop.
- Wait: chờ cửa sổ khớp Process/Title xuất hiện và lên trước rồi mới chạy các action con. **Timeout** (`TimeMs`) là thời gian chờ tối đa một lần, hết thì thử lại theo **Retry** (0 = không thử lại) rồi dừng với lỗi. **Wait** (`WaitMs`, mặc định 500 ms) là thời gian chờ sau khi thấy cửa sổ khớp: cửa sổ đó phải còn là cửa sổ khớp và đang ở trước thì mới chạy tiếp, để không nhầm với splash (Excel hiện "Opening - Excel" trước cửa sổ chính). File cũ chưa có `WaitMs` nạp với 500 và lưu thêm khi Save.
- Không còn cài đặt cấp kịch bản (start delay, lặp toàn bộ, target chung, Repeat từng bước): muốn lặp thì bọc trong Loop, muốn chờ thì dùng Delay.
- Runner đệ quy; biến lưu trong một `VariableStore` toàn cục cho mỗi lần chạy. Condition chạy case đầu tiên đúng, không có thì chạy else. Image lưu kết quả vào biến chứ không đổi ngữ cảnh của action con.
- **Record:** hook chuột/bàn phím cấp thấp ghi lại click, kéo, lăn chuột và phím rồi `RecordConverter` đổi thành action (Click, MouseDown/MouseUp, Scroll, KeyPress, ComboKey) với Delay là khoảng nghỉ; tọa độ màn hình, không Process.
- Lưu bằng JSON có trường `Version` (hiện là 2; file version 1 bị từ chối với thông báo rõ). Mọi thời gian lưu bằng mili giây (long); UI tự chọn đơn vị lớn nhất chia hết. Ảnh mẫu nằm ngay trong file (pixel nén Deflate + base64), không có file ảnh riêng.
- Chọn điểm/vùng bằng overlay toàn màn hình.

### Tọa độ theo cửa sổ
- Cửa sổ đích xác định bằng tên process, tiêu đề (cho phép regex) hoặc class.
- Lưu offset so với vùng client; lúc chạy lấy vị trí cửa sổ hiện tại rồi quy ra tọa độ màn hình. Vùng tìm ảnh cũng tính theo cách này.
- Mỗi điểm có thể chọn neo (trên-trái mặc định, trên-phải, dưới-trái, dưới-phải, giữa) để chịu được việc đổi kích thước cửa sổ; offset là số có dấu từ điểm neo (nằm trong cửa sổ khi neo phải/dưới thì là số âm).
- Image: một Anchor cho cả vùng tìm lẫn Position trả về (cùng góc của vùng và của ảnh, đo từ cùng góc cửa sổ); Mouse tự dùng Anchor của nó với giá trị đó.
- Khi pick vị trí: chương trình phải đang chạy (không thì từ chối), được đưa lên trước rồi mới ẩn cửa sổ của app để chọn.
- Khi chạy: nếu cửa sổ chưa ở foreground hoặc đang minimize thì tự khôi phục và đưa lên trước (best effort); báo lỗi và dừng nếu không tìm thấy cửa sổ.

### License
1. **Machine ID** = SHA-256 của `MachineGuid` (đọc registry HKLM, chỉ đọc, không cần quyền admin; view 64-bit), lấy 20 ký tự hex, dạng `XXXXX-XXXXX-XXXXX-XXXXX`. Không đổi theo thời gian, nằm trong license và được so với máy khi kiểm tra. Người dùng **không thấy** Machine ID.
2. **Request key** (cái người dùng gửi cho Admin): chương trình **mã hóa AES-128** Machine ID (kèm 6 byte mã kiểm tra) bằng khóa dẫn xuất từ một bí mật nhúng sẵn trong chương trình và ngày UTC `yyyyMMdd`, hiện 32 ký tự hex `XXXX-XXXX-...` (8 nhóm). Mỗi ngày một key khác; đầu ra không lộ Machine ID hay ngày (cửa sổ License không hiển thị ngày). Admin chạy `keys\make-license.bat`, nhập request key, ngày gửi (UTC, `YYYYMMDD`) và ngày hết hạn (`YYYYMMDD`, bỏ trống = vô thời hạn). KeyGen chỉ giải được khi ngày nhập **đúng khớp** với ngày tạo key; không khớp thì báo lỗi và không tạo file (admin tự quyết định ngày UTC). Kết quả là `keys\license.lic` (ghi đè nếu đã có). Giới hạn: khóa AES dẫn xuất từ bí mật nằm trong mã nguồn nên người dịch ngược chương trình vẫn tìm ra được; muốn chặt hơn thì chuyển sang mã hóa bất đối xứng (key dài hơn).
3. `license.lic`: JSON (Product, MachineId, User, Expires, Features, Issued = ngày UTC cấp) ký **ECDSA P-256 / SHA-256**; file gồm header, payload base64 và chữ ký base64 (88 ký tự cuối). Khi đọc, mọi khoảng trắng/xuống dòng sau header bị bỏ qua nên dán text bị chat/email ngắt dòng vẫn dùng được. App chỉ nhúng khóa công khai (`LicenseManager.PublicKey`). **Khóa riêng** nằm ở `keys\license-private.key` trong thư mục dự án; `.gitignore` chặn `/keys/*` (chỉ cho `keys\*.bat`), nên không bao giờ commit; không thuộc csproj nên build/clean không xóa; phải sao lưu ra nơi khác: mất khóa thì không cấp được license mới nếu không phát hành lại bản có khóa công khai mới.
4. **Không có license hợp lệ (thiếu, sai chữ ký, sai máy, hết hạn, đồng hồ sai, thiếu/hỏng Setting.ini với license có hạn) = khóa toàn bộ:** chương trình chỉ hiện màn hình License: request key + Copy, ô dán nội dung license ("Use pasted license"), "Import file...", Exit thì thoát. Cả hai cách đều kiểm tra rồi ghi `license.lic` **cạnh file exe**; copy chương trình sang nơi khác thì phải copy cả `license.lic` và `Setting.ini`, nếu không thì nhập lại.
5. **License có hạn** (license vô hạn bỏ qua toàn bộ mục này: không cần mạng, không có state):
   - **Kích hoạt (Import/dán) bắt buộc có mạng:** `TrustedClock` lấy giờ từ header `Date` của HTTPS (microsoft/google/cloudflare, kết nối có xác thực), không được thì NTP (time.windows.com, time.google.com, pool.ntp.org). Không có mạng thì từ chối. Hết hạn tính hết ngày `Expires` theo giờ chuẩn đó (đổi sang múi giờ máy).
   - **`Setting.ini`** cạnh exe, tạo tự động khi kích hoạt. Mục `[Window]` giữ vị trí, kích thước và trạng thái phóng to của cửa sổ; mục `[App]` có `State` = giờ UTC lần cuối dùng chương trình, **mã hóa AES-CBC + ký HMAC-SHA256** với khóa phụ thuộc máy và license (sửa tay, chép từ máy/license khác đều hỏng). Không dùng registry.
   - **Cập nhật state** lúc khởi động và sau New, Open, Save, Save As, Stop sau Start, Stop sau Record. Nếu đang **offline** và giờ máy sớm hơn state quá 2 phút thì báo lỗi (khóa); nếu **có mạng** thì giờ mạng là chuẩn và ghi đè state (đồng hồ máy từng sai không bị phạt mãi).
   - **Thiếu `Setting.ini`, thiếu `State` hoặc state hỏng** = license không hợp lệ: chỉ cần kích hoạt lại (Import hoặc dán), chương trình tự ghi đè license.lic và Setting.ini.
   - Sau lần lấy giờ mạng đầu tiên, giờ chạy tiếp bằng đồng hồ bấm giờ trong phiên nên chỉnh giờ máy giữa chừng vô hiệu; làm mới sau 1 giờ; nếu lần lấy giờ thất bại thì 10 phút sau mới thử lại. Offline vẫn dùng giờ máy và từ chối nếu sớm hơn ngày cấp (`Issued`) quá 1 ngày.
6. Kiểm tra lúc khởi động và lúc bấm Start, Start Selected, Record. Nút **License** trên menu bar: license có hạn thì cho xem trạng thái, request key và nhập license mới; license vô hạn thì chỉ hiện thông báo đã đăng ký vô hạn (không có request key hay phần nhập license).
- Giới hạn: .NET dễ bị decompile/vá, nên cơ chế này chỉ ngăn chia sẻ thông thường. Có thể tăng độ khó bằng obfuscation, kiểm tra nhiều chỗ, hoặc kích hoạt online có server thu hồi key (để giai đoạn sau).
- Khóa riêng không bao giờ được commit vào repo.

## 6. Kế hoạch (milestones)

1. Khung dự án (solution, lõi netstandard2.0, WinForms, manifest DPI) và engine chuột phím cơ bản.
2. Giao diện danh sách bước, overlay chọn điểm, lưu/mở JSON, phím nóng.
3. Tọa độ theo cửa sổ.
3b. Thiết kế lại thành cây action (Loop, Process/Anchor theo từng action, Scroll, Time/Delay có đơn vị) — đã xong, thay thế UI của M3.
4. Keyboard, Utilities/Open, Wait, Image, Variable, Condition, biểu thức, Record — đã xong và đã vào `preview`.
5. License theo máy và công cụ tạo key (KeyGen) — đã xong, đã vào `preview` và `main`.

## 7. Quy trình git (theo CLAUDE.md)

- `main` là production, `preview` là nhánh test, task mới tạo `feature_xxx` từ `preview`.
- Khi user nói "PR into preview" / "PR into production" thì tạo PR, merge ngay và chạy deploy/migration nếu có.
- Người dùng đã đồng ý tạo `preview` từ `main` khi bắt đầu milestone 1.

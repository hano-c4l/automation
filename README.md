# Automation Test

Công cụ automation / autoclick cho Windows (10 1903+ và 11). Chạy trên .NET Framework 4.8 (có sẵn trong Windows), người dùng cuối không phải cài thêm gì.

Người dùng soạn một kịch bản gồm các action xếp thành cây, rồi chạy (mặc định F3) hoặc ghi lại thao tác thật (mặc định F6); phím tắt đổi được trong Settings.

## Tính năng

- **Chuột:** Click, Mouse Down/Up, Move (có thời gian di chuyển), cuộn chuột; vị trí theo màn hình hoặc theo cửa sổ của chương trình đích (Process, Title, Anchor) để cửa sổ dời chỗ vẫn click đúng.
- **Bàn phím:** một phím, tổ hợp phím, hoặc cả đoạn text (kể cả tiếng Việt).
- **Điều khiển:** Loop (số lần, khoảng thời gian, tới thời điểm), Wait (chờ cửa sổ khớp, có Timeout, Retry và thời gian Wait để không nhầm splash), Image (nhận diện ảnh trong một vùng), Variable, Condition (switch-case), biểu thức `{biến}`; Utilities: Open để mở file hoặc chương trình, Active để máy không khóa hay tắt màn hình khi chạy lâu. Mỗi action bật/tắt được (State).
- **Start Selected:** chạy riêng các action đang chọn để thử nhanh (hỏi giá trị cho các biến mà phần chọn cần).
- **Record:** ghi lại click, kéo thả, cuộn và phím thành action; tùy chọn "Record all event" trong Settings để ghi cả di chuyển chuột.
- **Settings:** phím tắt (Start F3, Start Selected F4, Record F6, Pick F8) và hai tùy chọn, lưu trong `Setting.ini`.
- Lưu và mở kịch bản dạng JSON (ảnh mẫu nằm trong file), danh sách action thu gọn hoặc mở rộng theo nhánh.
- **License theo máy:** chương trình chỉ chạy khi có `license.lic` hợp lệ cho máy đó (có hoặc không có hạn dùng).

## Cấu trúc

```
AutomationTest.sln
  src/AutomationTest.Core     (netstandard2.0)  model kịch bản, JSON, engine chuột phím, so khớp ảnh, biểu thức, runner, kiểm tra license
  src/AutomationTest.App      (net48 WinForms)  giao diện, overlay chọn điểm/vùng, phím nóng, ghi thao tác
  tools/AutomationTest.KeyGen (net48 console)   công cụ cấp license, chỉ dành cho chủ sản phẩm, không phát hành cùng chương trình
  tools/AutomationTest.IconGen (net48 console)   vẽ lại icon: `dotnet run --project tools/AutomationTest.IconGen -- src/AutomationTest.App/app.ico`
  keys/                                      make-license.bat và khóa riêng (khóa riêng không được commit)
  docs/                                      PROJECT.md (yêu cầu và quyết định), STATUS.md (tình trạng hiện tại)
```

## Build và chạy

Cần .NET SDK (10.0 trở lên) để build; chạy chương trình chỉ cần Windows.

```bash
dotnet build -c Release
```

Chương trình nằm ở `src/AutomationTest.App/bin/Release/net48/AutomationTest.exe`. Tắt chương trình đang chạy trước khi build, nếu không file sẽ bị khóa.

## License

1. Lần đầu chạy, chương trình hiện màn hình License với **request key**; người dùng gửi key này cho admin.
2. Admin chạy `keys\make-license.bat`, nhập request key, ngày gửi key (UTC, `YYYYMMDD`) và ngày hết hạn (`YYYYMMDD`, để trống nếu vô thời hạn) để tạo `license.lic`. Cần build `tools/AutomationTest.KeyGen` trước và có khóa riêng `keys\license-private.key`.
3. Người dùng dán nội dung hoặc import file `license.lic` trong màn hình License. File được lưu cạnh file exe, nên copy chương trình sang nơi khác thì phải copy cả `license.lic` (và `Setting.ini` nếu license có hạn).

License có hạn cần mạng khi kích hoạt. Chi tiết xem mục License trong [docs/PROJECT.md](docs/PROJECT.md).

## Tài liệu

- [docs/PROJECT.md](docs/PROJECT.md): mục tiêu, quyết định kỹ thuật, mô hình kịch bản, license, kế hoạch.
- [docs/STATUS.md](docs/STATUS.md): tình trạng hiện tại, checklist test tay, ghi chú kỹ thuật.
- [CLAUDE.md](CLAUDE.md): quy ước làm việc (git, ngôn ngữ, subagent).

## Quy trình git

`main` là production, `preview` là nhánh test; việc mới làm trên nhánh `feature_xxx` tách từ `preview`.

## Bản Rust

Có bản viết lại bằng Rust (một file exe, không cần .NET) với cùng tính năng và cùng định dạng file: repo `automation_rust`, thư mục `D:\Working\AutomationRust`. Kịch bản JSON, `license.lic` và `Setting.ini` dùng chung được giữa hai bản.

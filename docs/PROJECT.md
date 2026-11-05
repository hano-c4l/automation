# Automation / AutoClick – Project Overview

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
Automation.sln
  src/Automation.Core     (netstandard2.0)  model kịch bản, JSON, engine chuột phím (P/Invoke),
                                            so khớp ảnh, tọa độ theo cửa sổ, kiểm tra license
  src/Automation.App      (net48 WinForms)  UI danh sách bước, overlay chọn điểm/vùng, phím nóng
  tools/Automation.KeyGen (net48 console)   công cụ tạo license, KHÔNG đóng gói cùng bản phát hành
```

## 4. Điểm kỹ thuật bắt buộc

1. **DPI:** khai báo Per-Monitor V2 trong manifest. `SendInput` dùng tọa độ chuẩn hóa 0–65535; nhiều màn hình thì dùng `MOUSEEVENTF_VIRTUALDESK`.
2. **Gõ tiếng Việt:** `SendInput` với `KEYEVENTF_UNICODE`; dự phòng bằng clipboard + Ctrl+V. Unikey/EVKey đang bật có thể làm méo text.
3. **UIPI:** chỉ điều khiển được cửa sổ admin khi chương trình cũng chạy admin. Cần tùy chọn chạy admin.
4. **Move theo thời gian:** nội suy có easing trong thời gian đã đặt, dùng `Stopwatch` + `timeBeginPeriod(1)`; không dùng `Thread.Sleep` đơn thuần.
5. **Nhận diện ảnh:** so khớp mẫu (ưu tiên tự viết NCC bằng C# cho nhẹ; OpenCvSharp thêm ~50–60MB nên là phương án dự phòng) với ngưỡng giống nhau, chỉ tìm trong vùng đã định nghĩa.
6. **An toàn:** phím nóng toàn cục Start/Stop/Panic (`RegisterHotKey`, ví dụ F6/F7/Esc); tùy chọn dừng khẩn cấp khi đưa chuột vào góc màn hình.
7. **SmartScreen/antivirus:** exe chưa ký gọi `SendInput` có thể bị cảnh báo. Người dùng chọn "More info → Run anyway" hoặc thêm Defender exclusion.

## 5. Mô hình kịch bản và file lưu

- Kịch bản là danh sách bước: Click trái/phải, Mouse-down, Mouse-up, Move (kèm duration), Key, Text, Wait, `IfImage → then/else`, Repeat, Label/Goto.
- Mỗi bước: tọa độ, thời gian thực hiện, thời gian chờ sau bước, số lần lặp.
- Lưu bằng JSON có trường `version`; ảnh mẫu để trong thư mục cạnh file kịch bản, hoặc đóng gói chung `.zip`.
- Chọn điểm/vùng bằng overlay toàn màn hình.

### Tọa độ theo cửa sổ
- Cửa sổ đích xác định bằng tên process, tiêu đề (cho phép regex) hoặc class.
- Lưu offset so với vùng client; lúc chạy lấy vị trí cửa sổ hiện tại rồi quy ra tọa độ màn hình. Vùng tìm ảnh cũng tính theo cách này.
- Mỗi điểm có thể chọn neo (trên-trái mặc định, phải/dưới/giữa) để chịu được việc đổi kích thước cửa sổ; không có neo phù hợp thì vẫn có thể lệch.
- Tùy chọn: đưa cửa sổ lên foreground trước khi chạy; báo lỗi và dừng nếu không tìm thấy cửa sổ hoặc nó đang minimize.

### License
1. App tạo **Machine ID** bằng cách băm thông tin ổn định (như `MachineGuid` trong registry, serial ổ đĩa/mainboard).
2. Người dùng gửi Machine ID; chủ sản phẩm dùng `KeyGen` tạo file license gồm Machine ID, ngày hết hạn (hoặc không hết hạn), tên người dùng, tính năng, rồi ký bằng khóa riêng (ECDSA P-256 hoặc RSA, có sẵn trong .NET Framework 4.8).
3. App chỉ nhúng khóa công khai để kiểm tra chữ ký.
4. Chống lùi đồng hồ: lưu mốc thời gian chạy gần nhất, từ chối nếu giờ hệ thống lùi.
- Giới hạn: .NET dễ bị decompile/vá, nên cơ chế này chỉ ngăn chia sẻ thông thường. Có thể tăng độ khó bằng obfuscation, kiểm tra nhiều chỗ, hoặc kích hoạt online có server thu hồi key (để giai đoạn sau).
- Khóa riêng không bao giờ được commit vào repo.

## 6. Kế hoạch (milestones)

1. Khung dự án (solution, lõi netstandard2.0, WinForms, manifest DPI) và engine chuột phím cơ bản.
2. Giao diện danh sách bước, overlay chọn điểm, lưu/mở JSON, phím nóng.
3. Tọa độ theo cửa sổ.
4. Gửi phím, gửi text, nhận diện ảnh và điều kiện.
5. License theo máy và công cụ tạo key.

## 7. Quy trình git (theo CLAUDE.md)

- `main` là production, `preview` là nhánh test, task mới tạo `feature_xxx` từ `preview`.
- Khi user nói "PR into preview" / "PR into production" thì tạo PR, merge ngay và chạy deploy/migration nếu có.
- Người dùng đã đồng ý tạo `preview` từ `main` khi bắt đầu milestone 1.

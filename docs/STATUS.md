# Status

Cập nhật lần cuối: 2026-11-05

## Hiện tại
- Người dùng yêu cầu **thiết kế lại cơ chế** thành cây action (xem `docs/PROJECT.md` mục 5). Phần Khung + Mouse + Loop đã code xong, build sạch, Core đã kiểm tra bằng script; **chờ người dùng test giao diện**.
- Nhánh đang làm: `feature_action_tree` (cắt từ `feature_m3_window_coords` vì thiết kế mới dùng lại code cửa sổ của M3, và M3 chưa merge). Chưa push. Khi người dùng nói "PR into preview" thì PR nhánh này vào `preview`; nó chứa cả M3 nên không cần PR riêng cho `feature_m3_window_coords`.
- `preview` hiện có M1 + M2 (kịch bản dạng danh sách bước phẳng).

## Đã làm
- [x] Yêu cầu, công nghệ (C# / .NET Framework 4.8 / WinForms, chỉ Windows), .NET SDK 10.0.403 để build.
- [x] M1 (engine chuột), M2 (UI, JSON, runner, phím nóng F6/Esc): đã merge vào `preview`, người dùng đã test.
- [x] M3 (tọa độ theo cửa sổ): code + test Core xong, nay được gộp vào thiết kế mới.
- [x] **Cây action (version 2 của JSON):**
  - Core: `ActionNode` (một lớp, Category/Action/Button/Time/Delay/Process/Anchor/X/Y/ScrollNotches, Loop với Count/Duration/Until, `Children`), `AutomationScript` (chỉ có `Actions`), `ScriptSerializer` (từ chối file version 1), `ScriptRunner` đệ quy với `RunContext`, `MouseController.Scroll`, `WindowLocator.IsForeground`.
  - App: thanh công cụ New/Open/Save/Start-Stop; danh sách thụt lề chọn nhiều; panel phải có Add/Update/Delete/In/Out/Up/Down, Category, form Mouse và form Loop, `TimeBox` (số + đơn vị ms/s/min/h), Pick program, Pick position.
  - Đã kiểm tra bằng script PowerShell nạp DLL: JSON round-trip lồng nhau giữ giá trị 0 (Time=0, Delay=0), file version 1 bị từ chối, Loop theo số lần/khoảng thời gian (thân không delay vẫn không treo)/tới thời điểm (quá khứ thì bỏ qua kèm thông báo), hủy giữa chừng, click theo cửa sổ với Anchor BottomRight trong Loop (khớp từng pixel, dời cửa sổ vẫn trúng), báo lỗi khi không thấy cửa sổ.

## Việc tiếp theo
- [ ] Người dùng test giao diện (checklist dưới), báo lỗi nếu có.
- [ ] PR vào `preview` khi người dùng yêu cầu.
- [ ] Keyboard, Command/Open, Wait, Image, Record; License (M5).

### Checklist test giao diện
1. Mở app: thanh công cụ ở trên, danh sách bên trái, panel phải có hàng nút Add/Update/Delete và In/Out/Up/Down; không bị cắt nút nào (thử scale 125%/150%).
2. Category = Mouse: chọn Action = ScrollUp/ScrollDown thì Type chỉ còn Middle và hiện ô "Scroll notches"; đổi lại Click thì có đủ Left/Right/Middle.
3. Time/Delay mặc định 500 milliseconds; đổi đơn vị sang seconds, thêm action: cột Time/Delay hiện đúng ("2 s").
4. Process trống: Anchor bị tắt, X/Y là tọa độ màn hình; "Pick position..." chọn được điểm bất kỳ.
5. "Pick program..." click vào một chương trình (ví dụ Notepad): ô Process được điền. Nhập Anchor, rồi "Pick position...": chương trình được đưa lên trước, chọn điểm trong nó thì X/Y là offset nhỏ. Tắt chương trình rồi Pick position: bị từ chối. Click ngoài chương trình/thanh tiêu đề: báo lỗi, không lưu.
6. Category = Control (Loop): đổi "Loop by" giữa Count / Duration / Until thì hiện đúng ô tương ứng.
7. Chọn Loop rồi Add một action: nó nằm trong Loop (thụt lề). Chọn một action bên dưới Loop và bấm In: vào trong Loop; bấm Out: ra ngoài, nằm ngay sau Loop.
8. Chọn nhiều action cùng cấp: Up/Down/Delete hoạt động; chọn khác cấp rồi Up/Down/In/Out thì báo "cùng danh sách". Update đổi Loop sang Mouse khi còn con thì bị từ chối.
9. Start (F6): app thu nhỏ, chạy đúng thứ tự trong Loop; Esc/F6 dừng; sau Start với action có Process thì cửa sổ được đưa lên trước và click đúng chỗ kể cả khi đã kéo cửa sổ chỗ khác.
10. Save rồi Open lại: cây, Process, Anchor, Time/Delay và Loop giữ nguyên. Mở file cũ (version 1) thì báo không hỗ trợ.

### Ghi chú cho milestone sau
- Trường mới có giá trị mặc định khác 0 phải đặt trong `[OnDeserializing] SetDefaults` của `ActionNode`/`AutomationScript` và không dùng `EmitDefaultValue=false` cho chúng (giá trị 0 do người dùng đặt sẽ bị mất khi nạp lại).
- Keyboard: `RegisterHotKey` có thể bắt cả phím do `SendInput` tạo ra, nên action gửi F6/Esc có thể tự dừng kịch bản. Cần bỏ qua hotkey khi runner đang gửi phím, hoặc dùng tổ hợp có modifier làm phím dừng.
- Image sẽ đặt `RunContext` mới cho các action con (cửa sổ + vị trí ảnh làm gốc); action con của Image kế thừa Process của Image theo spec.
- Khi Process khớp nhiều cửa sổ, runner lấy cửa sổ khớp đầu tiên theo z-order (ưu tiên không minimize); cần điền Title (regex) để chỉ rõ.
- Đổi Anchor sau khi pick sẽ giữ nguyên số X/Y nhưng đổi nghĩa; phải pick lại.
- Multi-select giữ lại bằng ListView thụt lề (TreeView của WinForms không chọn nhiều được). Chưa có thu gọn/mở rộng nhánh.

## Ghi chú môi trường
- Khi Claude chạy exe từ công cụ của nó, Windows có lúc báo "An Application Control policy has blocked this file", nhưng người dùng chạy trực tiếp thì bình thường. Nguyên nhân chưa xác định; cần thử trên máy sạch trước khi phát hành.
- Build lỗi "file is being used" khi exe đang chạy: tắt app trước khi build (hoặc build ra thư mục khác bằng `-p:OutDir=`).
- Lỗi cú pháp heredoc nhiều dòng trong Bash tool: tạo file bằng công cụ Write thay vì `cat <<EOF`.

## Mở / lưu ý
- Ký số: chưa quyết định, xem lại khi phát hành rộng.
- Kích hoạt license online (server thu hồi key): để giai đoạn sau.
- Overlay chọn điểm chưa thử trên cấu hình nhiều màn hình có DPI khác nhau.
- `.claude/launch.json` còn trỏ tới server npm "client-dev" không thuộc dự án này; cần sửa hoặc xóa.

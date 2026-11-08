import AppKit

/// One row of lyrics sized for the Touch Bar (30pt tall): the current line in
/// white, optionally followed by the next line dimmed, plus a small badge at
/// the right edge. Crossfades between lines; lines too wide to fit are shrunk
/// a little, then scrolled.
final class LyricView: NSView {
    /// Called with `true` when the view is put on the Touch Bar and `false`
    /// when it is removed (minimised, dismissed, or closed with the system ⊗).
    var onVisibilityChange: ((Bool) -> Void)?

    /// Clips the lyric row so scrolled text never runs under the badge.
    private let clip = NSView()
    private let primary = NSTextField(labelWithString: "")
    private let next = NSTextField(labelWithString: "")
    private let badge = NSTextField(labelWithString: "")
    private var display: Display?
    private var flashText: String?
    private var flashWork: DispatchWorkItem?
    private var scrollWork: DispatchWorkItem?

    private let gap: CGFloat = 18
    private let inset: CGFloat = 10
    private static let accent = NSColor(calibratedRed: 0.11, green: 0.73, blue: 0.33, alpha: 1)

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.masksToBounds = true
        clip.wantsLayer = true
        clip.layer?.masksToBounds = true
        addSubview(clip)
        for label in [primary, next, badge] {
            label.lineBreakMode = .byClipping
            label.maximumNumberOfLines = 1
            label.cell?.truncatesLastVisibleLine = false
        }
        clip.addSubview(primary)
        clip.addSubview(next)
        addSubview(badge)
        next.textColor = NSColor(white: 1, alpha: 0.45)
        badge.font = .systemFont(ofSize: 11, weight: .semibold)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        onVisibilityChange?(window != nil)
    }

    func show(_ newValue: Display) {
        guard newValue != display else { return }
        let changedLine = newValue.primary != display?.primary
        display = newValue
        if changedLine, window != nil {
            let fade = CATransition()
            fade.type = .fade
            fade.duration = 0.18
            layer?.add(fade, forKey: "fade")
        }
        needsLayout = true
    }

    /// Briefly shows `text` (e.g. "+0.5 s") as a highlighted badge.
    func flash(_ text: String) {
        flashText = text
        flashWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            self?.flashText = nil
            self?.needsLayout = true
        }
        flashWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6, execute: work)
        needsLayout = true
    }

    override func layout() {
        super.layout()
        scrollWork?.cancel()
        guard let display else { return }
        let midY = { (label: NSTextField) in ((self.bounds.height - label.frame.height) / 2).rounded() }

        // Badge (flash wins over the status badge).
        let badgeText = flashText ?? display.badge
        badge.isHidden = badgeText == nil
        badge.stringValue = badgeText ?? ""
        badge.textColor = flashText != nil ? Self.accent : NSColor(white: 1, alpha: 0.5)
        badge.sizeToFit()
        let badgeSpace = badgeText == nil ? 0 : badge.frame.width + 12
        badge.setFrameOrigin(NSPoint(x: bounds.width - inset - badge.frame.width, y: midY(badge)))

        let base = Settings.fontSize
        let isInfo = display.style == .info
        primary.textColor = isInfo ? NSColor(white: 1, alpha: 0.75) : .white
        primary.stringValue = display.primary

        let available = max(0, bounds.width - inset * 2 - badgeSpace)
        clip.frame = NSRect(x: inset, y: 0, width: available, height: bounds.height)
        // Shrink up to 2pt before resorting to scrolling.
        var size = base
        repeat {
            primary.font = .systemFont(ofSize: size, weight: isInfo ? .medium : .semibold)
            primary.sizeToFit()
            if primary.frame.width <= available { break }
            size -= 1
        } while size >= base - 2

        let showNext = (Settings.showNextLine || isInfo) && !(display.next ?? "").isEmpty
        next.font = .systemFont(ofSize: max(10, size - 2), weight: .regular)
        next.stringValue = showNext ? display.next! : ""
        next.sizeToFit()

        let primaryWidth = primary.frame.width
        let nextWidth = showNext ? next.frame.width : 0
        let rowWidth = primaryWidth + (showNext ? gap + nextWidth : 0)

        if rowWidth <= available {
            // Everything fits: centre the row in the space left of the badge.
            let x = ((available - rowWidth) / 2).rounded()
            primary.setFrameOrigin(NSPoint(x: x, y: midY(primary)))
            next.isHidden = !showNext
            next.setFrameOrigin(NSPoint(x: x + primaryWidth + gap, y: midY(next)))
        } else if primaryWidth <= available {
            // Only the current line fits.
            next.isHidden = true
            primary.setFrameOrigin(NSPoint(x: ((available - primaryWidth) / 2).rounded(), y: midY(primary)))
        } else {
            next.isHidden = true
            primary.setFrameOrigin(NSPoint(x: 0, y: midY(primary)))
            scheduleScroll(overflow: primaryWidth - available, lineDuration: display.lineDuration)
        }
    }

    /// Scrolls an overlong line so its end is visible before the line changes.
    private func scheduleScroll(overflow: CGFloat, lineDuration: Double?) {
        let hold = 0.8
        let time = max(1.2, min(Double(overflow) / 35, (lineDuration ?? 6) - hold - 0.6))
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = time
                ctx.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                self.primary.animator().setFrameOrigin(NSPoint(x: -overflow, y: self.primary.frame.minY))
            }
        }
        scrollWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + hold, execute: work)
    }
}

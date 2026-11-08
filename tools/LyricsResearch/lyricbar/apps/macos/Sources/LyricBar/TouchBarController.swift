import AppKit
import TouchBarPrivate

/// Puts a ♪ button in the Control Strip; tapping it shows lyrics across the
/// whole Touch Bar (system-modal bar). The system ⊗ on the left closes it
/// again, after which the ♪ button reappears in the Control Strip.
final class TouchBarController: NSObject, NSTouchBarDelegate {
    static let trayID = NSTouchBarItem.Identifier("io.github.lyricbar.tray")
    static let lyricID = NSTouchBarItem.Identifier("io.github.lyricbar.lyric")

    let isAvailable = LBTouchBarAvailable()
    /// Whether lyrics are currently on the Touch Bar. Derived from the lyric
    /// view entering/leaving the Touch Bar window, so it also catches the
    /// user closing the bar with the system ⊗ (which has no callback).
    private(set) var isVisible = false
    var onVisibilityChange: (() -> Void)?

    // Frame-based (autoresizing) view: the Touch Bar stretches it to fill all
    // the space left of the Control Strip. Don't add width constraints.
    private let lyricView = LyricView(frame: NSRect(x: 0, y: 0, width: 600, height: 30))
    private var trayItem: NSCustomTouchBarItem?
    private lazy var modalBar: NSTouchBar = {
        let bar = NSTouchBar()
        bar.delegate = self
        bar.defaultItemIdentifiers = [Self.lyricID]
        return bar
    }()

    override init() {
        super.init()
        lyricView.onVisibilityChange = { [weak self] visible in
            guard let self, self.isVisible != visible else { return }
            self.isVisible = visible
            self.onVisibilityChange?()
        }
    }

    func install() {
        guard isAvailable else {
            NSLog("[LyricBar] Touch Bar APIs unavailable — menu bar only")
            return
        }
        let item = NSCustomTouchBarItem(identifier: Self.trayID)
        let button = NSButton(
            image: symbol("music.note", fallback: "♪"),
            target: self,
            action: #selector(show)
        )
        button.bezelColor = NSColor(calibratedRed: 0.11, green: 0.73, blue: 0.33, alpha: 1)
        item.view = button
        trayItem = item
        LBSetSystemModalShowsCloseBox(true)
        LBAddControlStripItem(item)
    }

    func uninstall() {
        LBDismissSystemModal(modalBar)
        if let trayItem { LBRemoveControlStripItem(trayItem) }
    }

    @objc func show() {
        guard isAvailable else { return }
        LBPresentSystemModal(modalBar, Self.trayID)
    }

    func hide() {
        LBMinimizeSystemModal(modalBar)
    }

    func toggle() {
        isVisible ? hide() : show()
    }

    func update(_ display: Display) {
        lyricView.show(display)
    }

    func flash(_ text: String) {
        lyricView.flash(text)
    }

    func touchBar(_ touchBar: NSTouchBar, makeItemForIdentifier identifier: NSTouchBarItem.Identifier) -> NSTouchBarItem? {
        switch identifier {
        case Self.lyricID:
            let item = NSCustomTouchBarItem(identifier: identifier)
            item.view = lyricView
            return item
        default:
            return nil
        }
    }

    private func symbol(_ name: String, fallback: String) -> NSImage {
        if let image = NSImage(systemSymbolName: name, accessibilityDescription: fallback) { return image }
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            (fallback as NSString).draw(in: rect, withAttributes: [.font: NSFont.systemFont(ofSize: 14), .foregroundColor: NSColor.white])
            return true
        }
        return image
    }
}

# dmgbuild settings for QuickPrompt.dmg: a small window with the app on the left, an arrow, and the
# Applications folder on the right (drag to install). Used by build-app.sh.
#   python -m dmgbuild -s dmg-settings.py -D app=... -D icon=... "Quick Prompt" QuickPrompt.dmg
import os.path

app = defines["app"]  # noqa: F821 (provided by dmgbuild)
appname = os.path.basename(app)

format = "UDZO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
icon = defines["icon"]  # noqa: F821  volume icon (shown on the desktop while mounted)

background = defines["background"]  # noqa: F821  a plain light picture with a thin arrow (1x + 2x TIFF)
window_rect = ((200, 140), (560, 340))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 112
text_size = 13
icon_locations = {
    appname: (150, 150),
    "Applications": (410, 150),
    # Hidden files go far outside the window, so they stay out of sight even when Finder shows hidden files.
    ".background.tiff": (150, 900),
    ".VolumeIcon.icns": (410, 900),
}

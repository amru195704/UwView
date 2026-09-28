cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.7.3.5"
  sha256 arm:   "b564220fcd5b7c06134831cecf5c2f49aceacd59b0b99e30411cbabf44ad599b",
         intel: "fceee70796c874bf2ff071bad4a9e7df6599c6a89cfe66873b37f4c87c081c55"

  url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-mac-#{arch}.dmg"
  name "UwView"
  desc "巨大なログ・テキストを調べるビューア（GUI本体 + CLIの uvf を同梱）"
  homepage "https://uvp.y42u.net/"

  depends_on macos: ">= :big_sur"

  app "UwView.app"
  binary "#{appdir}/UwView.app/Contents/MacOS/uvf"

  zap trash: [
    "~/Library/Preferences/net.y42u.uwview.plist",
  ]
end

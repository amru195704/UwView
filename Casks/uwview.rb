cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.6.6.1"
  sha256 arm:   "987c7dbd7cda539cdc804da39f76929b58842574e083ba39173957b2742e436f",
         intel: "9a7a1e06cb838ab25d5a7af099444fa34bc1dd993d48a743e56dcb47a005d2a4"

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

cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.7.3.6"
  sha256 arm:   "b36eba75e25cc73ba20d2302437418e00e9e1a33875b1b94ec34876d932b3612",
         intel: "bc306b356a6d49f874212ed66ea69a0d845cf8ddcf25d45daf763006e7bef375"

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

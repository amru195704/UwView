cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "2.0.0"
  sha256 arm:   "858fa142f871c490ee86f2092b845a89786e5b94ea7af1dd5373feca10bed31d",
         intel: "2cb6cd273b7231938ca841645cf2cd7c44fe926d8c809b5e98aaafc500c28538"

  url "https://github.com/amru195704/UwView/releases/download/v#{version}/UwView-#{version}-mac-#{arch}.dmg"
  name "UwView"
  desc "Open and search huge text/log files, with the uvf command"
  homepage "https://github.com/amru195704/UwView"

  livecheck do
    url :url
    strategy :github_latest
  end

  app "UwView.app"
  binary "#{appdir}/UwView.app/Contents/MacOS/uvf"
end

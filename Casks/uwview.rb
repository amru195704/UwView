cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.9.0"
  sha256 arm:   "cf360a106f42ee46267a0ef98e5109767964d1df82a152ffedc5d51d75d01f77",
         intel: "05dba5a732e0f201460a481905e091ca86718b97e28f528d07d3c0124f400e54"

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

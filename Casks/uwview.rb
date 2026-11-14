cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "2.0.0"
  sha256 arm:   "c41e03fa6689b0d3cdc0c911a005013e6104a9c74fa685eaf00926ff97781ad8",
         intel: "4f76a0eb73b3d692317cfdc1717f1c7eca3287c7c7f754d349ccc2b26d099be8"

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

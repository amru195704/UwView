cask "uwview" do
  arch arm: "arm64", intel: "x64"

  version "1.8.1.9"
  sha256 arm:   "d98bb2dcfdbab175763af3dabd523356be0da10a1809d6e0f095ce3c26e44fb1",
         intel: "bc7f89ff811f2a7a89d7681be17925095b8bf8fb15f14b055997a8bed8c05f47"

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

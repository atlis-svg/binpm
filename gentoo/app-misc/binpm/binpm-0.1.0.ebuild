# Copyright 2026 Gentoo Authors
# Distributed under the terms of the GNU General Public License v2

EAPI=8

DESCRIPTION="A package manager for binaries: installs executables from GitHub releases and HTTP registries"
HOMEPAGE="https://github.com/atlis-svg/binpm"
SRC_URI="https://github.com/atlis-svg/binpm/releases/download/v${PV}/${P}-${PV}-source.tar.gz -> ${P}.tar.gz"
S="${WORKDIR}/${PN}-${PV}"

LICENSE="MIT"
SLOT="0"
KEYWORDS="~amd64 ~x86"

# The CLI is a .NET application: it needs the SDK to build and the runtime to run.
DEPEND="virtual/dotnet-sdk:8.0"
RDEPEND="virtual/dotnet-sdk:8.0"

# The test suite restores xunit from NuGet, which needs network access.
RESTRICT="!test? ( test )"

src_compile() {
	local dotnet_home

	dotnet_home="$(dirname "$(readlink -f "$(command -v dotnet)")")"
	export DOTNET_ROOT="${dotnet_home}"
	export DOTNET_CLI_TELEMETRY_OPTOUT=1
	export DOTNET_NOLOGO=1
	export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
	export HOME="${WORKDIR}/home"
	export NUGET_PACKAGES="${WORKDIR}/nuget-packages"

	mkdir -p "${HOME}" "${NUGET_PACKAGES}"

	einfo "Publishing BinPm ${PV}"
	dotnet publish src/BinPm.Cli/BinPm.Cli.csproj \
		--configuration Release \
		--output "${WORKDIR}/publish" \
		--nologo \
		-p:Version="${PV}" \
		|| die "dotnet publish failed"
}

src_install() {
	dodir "/usr/lib/${PN}"
	cp -r "${WORKDIR}/publish/." "${ED}/usr/lib/${PN}/" || die

	newbin "${FILESDIR}/${PN}.wrapper" binpm
}

pkg_postinst() {
	elog
	elog "BinPm installs the commands it manages into ~/.local/bin by default."
	elog "If that directory is not on your PATH, add it:"
	elog
	elog "    export PATH=\"\$HOME/.local/bin:\$PATH\""
	elog
}

#!/usr/bin/env bash

get_changelog_items() {
    awk '/^## /{count++; next} count==1' "$1" | sed '1d;$d'
}

get_package_name() {
    head -n 1 "$1" | awk '{print $2}'
}

get_package_version() {
    grep "\\S" "$1" | sed -n '2p' | awk '{print $2}'
}

nuget_link() {
    printf "https://www.nuget.org/packages/%s/%s\n" "$1" "$2"
}

release_notes() {
    package_name=$(get_package_name "$1")
    package_version=$(get_package_version "$1")

    # Print changelog items
    printf "# Changelog\n\n"
    get_changelog_items "$1"

    # Print package link
    printf "\n# Links\n\n"
    nuget_link "$package_name" "$package_version"
}

release_notes "$1"

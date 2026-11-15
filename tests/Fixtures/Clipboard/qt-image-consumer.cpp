// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-License-Identifier: BSD-3-Clause
#include <QtGui/QGuiApplication>
#include <QtGui/QClipboard>
#include <QtGui/QImage>
#include <QtWidgets/QApplication>
#include <QtWidgets/QWidget>
#include <QtCore/QFile>
#include <QtCore/QMimeData>
#include <QtCore/QTimer>
#include <cstdio>

// Run only against a private test compositor, never the user's clipboard.
int main(int argc, char **argv) {
    QApplication app(argc, argv);
    if (argc != 3 || qEnvironmentVariable("POOKIE_ISOLATED_CLIPBOARD") != "1") return 2;
    const auto path = QString::fromLocal8Bit(argv[1]);
    const auto signal = QString::fromLocal8Bit(argv[2]);
    QWidget window;
    window.setWindowTitle("Pookie isolated Qt paste fixture");
    window.resize(400, 240);
    window.show();
    window.activateWindow();
    bool reading = false;
    QTimer timer;
    QObject::connect(&timer, &QTimer::timeout, [&] {
        if (reading || !window.isActiveWindow()) return;
        reading = true;
        QFile started(signal);
        if (!started.open(QIODevice::WriteOnly)) { app.exit(3); return; }
        started.write("ready"); started.close();
        const auto mime = QGuiApplication::clipboard()->mimeData();
        if (!mime || !mime->hasImage() || mime->hasText()) { std::fprintf(stderr, "QT_PASTE: image missing or exposed as text\n"); app.exit(4); return; }
        const auto actual = qvariant_cast<QImage>(mime->imageData()).convertToFormat(QImage::Format_RGBA8888);
        const auto expected = QImage(path).convertToFormat(QImage::Format_RGBA8888);
        if (actual.isNull() || expected.isNull() || actual != expected) {
            actual.save(signal + ".received.png");
            std::fprintf(stderr, "QT_PASTE: corrupt/incomplete image actual=%dx%d expected=%dx%d formats=%s\n",
                actual.width(), actual.height(), expected.width(), expected.height(), qPrintable(mime->formats().join(',')));
            app.exit(5); return;
        }
        std::printf("QT_IMAGE_READ_OK backend=%s pixels=%dx%d\n", qPrintable(QGuiApplication::platformName()), actual.width(), actual.height());
        app.exit(0);
    });
    timer.start(50);
    QTimer::singleShot(12000, [&] { app.exit(6); });
    return app.exec();
}

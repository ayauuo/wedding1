(function () {
  var params = new URLSearchParams(window.location.search);
  var imgUrl = params.get('img') || '';
  var videoUrl = params.get('video') || '';

  var panelPhoto = document.getElementById('panel-photo');
  var panelVideo = document.getElementById('panel-video');
  var photoImg = document.getElementById('photo-img');
  var photoVid = document.getElementById('photo-video');
  var photoDl = document.getElementById('photo-dl');
  var videoDl = document.getElementById('video-dl');
  var hint = document.getElementById('hint');
  var tabs = document.querySelectorAll('.tab');

  function showPanel(name) {
    var isPhoto = name === 'photo';
    panelPhoto.classList.toggle('is-visible', isPhoto);
    panelPhoto.setAttribute('aria-hidden', isPhoto ? 'false' : 'true');
    panelVideo.classList.toggle('is-visible', !isPhoto);
    panelVideo.setAttribute('aria-hidden', isPhoto ? 'true' : 'false');
    tabs.forEach(function (t) {
      t.classList.toggle('is-active', t.getAttribute('data-tab') === name);
      t.setAttribute('aria-pressed', t.getAttribute('data-tab') === name ? 'true' : 'false');
    });
  }

  if (imgUrl) {
    photoImg.src = imgUrl;
    photoImg.style.display = 'block';
    photoDl.href = imgUrl;
    photoDl.download = 'photo.jpg';
    photoDl.classList.remove('is-hidden');
  } else {
    photoImg.style.display = 'none';
    photoDl.classList.add('is-hidden');
  }

  if (videoUrl) {
    photoVid.src = videoUrl;
    videoDl.href = videoUrl;
    videoDl.download = 'video.webm';
    videoDl.classList.remove('is-hidden');
  } else {
    panelVideo.querySelector('.media-wrap').style.display = 'none';
    videoDl.classList.add('is-hidden');
  }

  if (imgUrl || videoUrl) {
    hint.classList.add('is-hidden');
    if (!imgUrl && videoUrl) showPanel('video');
    else showPanel('photo');
  } else {
    showPanel('photo');
    hint.classList.remove('is-hidden');
  }

  tabs.forEach(function (btn) {
    btn.addEventListener('click', function () {
      showPanel(btn.getAttribute('data-tab'));
    });
  });
})();
